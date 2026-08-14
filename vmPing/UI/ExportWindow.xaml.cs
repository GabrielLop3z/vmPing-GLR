using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.Win32;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class ExportWindow : Window
    {
        private readonly ObservableCollection<Probe> _probes;
        private readonly List<ExportRow> _rows = new List<ExportRow>();

        public ExportWindow(ObservableCollection<Probe> probes)
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _probes = probes ?? new ObservableCollection<Probe>();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshRows();
        }

        private void Range_Changed(object sender, RoutedEventArgs e)
        {
            RefreshRows();
        }

        private DateTime GetWindowStart()
        {
            if (rb30Min.IsChecked == true)
                return DateTime.Now.AddMinutes(-30);
            return DateTime.MinValue;
        }

        private void RefreshRows()
        {
            _rows.Clear();

            DateTime windowStart = GetWindowStart();

            foreach (var probe in _probes)
            {
                var samples = probe.PingSamples.Where(s => s.Timestamp >= windowStart).ToList();
                var row = BuildRow(probe, samples);
                _rows.Add(row);
            }

            // Ordenar: Fallando → Intermitente → Bien → Sin datos
            dgExport.ItemsSource = _rows.OrderBy(r => r.SortRank).ToList();
            lblSubtitle.Text = rb30Min.IsChecked == true
                ? $"Historial de conexión (últimos 30 min). {_rows.Count} hosts."
                : $"Historial de conexión completo de la sesión. {_rows.Count} hosts.";
        }

        private ExportRow BuildRow(Probe probe, List<PingSample> samples)
        {
            var row = new ExportRow
            {
                Hostname = probe.Hostname,
                Alias = probe.Alias,
                CurrentStatus = StatusToText(probe.Status),
                Sent = samples.Count,
                Received = samples.Count(s => s.Success),
            };
            row.Lost = row.Sent - row.Received;

            var ok = samples.Where(s => s.Success).Select(s => s.RttMs).ToList();
            if (ok.Count > 0)
            {
                row.MinRtt = ok.Min();
                row.MaxRtt = ok.Max();
                row.AvgRtt = ok.Average();
            }

            if (row.Sent == 0)
            {
                row.Classification = "Sin datos";
            }
            else if (row.Sent > 0 && row.Lost == 0)
            {
                row.Classification = "Bien";
            }
            else if (probe.Status == ProbeStatus.Down || probe.Status == ProbeStatus.Error)
            {
                row.Classification = "Fallando";
            }
            else if (row.Lost >= row.Sent)
            {
                row.Classification = "Fallando";
            }
            else
            {
                row.Classification = "Intermitente";
            }

            row.LossPct = row.Sent == 0 ? 0 : (100.0 * row.Lost / row.Sent);
            row.SortRank = row.Classification == "Fallando" ? 0
                : row.Classification == "Intermitente" ? 1
                : row.Classification == "Bien" ? 2 : 3;

            return row;
        }

        private string StatusToText(ProbeStatus status)
        {
            switch (status)
            {
                case ProbeStatus.Up: return "Activo";
                case ProbeStatus.Down: return "Caído";
                case ProbeStatus.Error: return "Error";
                case ProbeStatus.LatencyHigh: return "Latencia alta";
                case ProbeStatus.LatencyNormal: return "Latencia normal";
                case ProbeStatus.Indeterminate: return "Indeterminado";
                case ProbeStatus.Inactive: return "Inactivo";
                case ProbeStatus.Scanner: return "Escáner";
                case ProbeStatus.Start: return "Inicio";
                case ProbeStatus.Stop: return "Detenido";
                default: return status.ToString();
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Guardar reporte (Excel)",
                Filter = "Archivo Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                FileName = $"vmPing_Reporte_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    // Sheet 1: Resumen (clasificación por host)
                    var ws = wb.Worksheets.Add("Resumen");

                    string[] headers = { "Hostname", "Alias", "Estado", "Clasificación", "Enviados", "Recibidos", "Perdidos", "% Pérdida", "Min RTT (ms)", "Prom RTT (ms)", "Max RTT (ms)" };
                    for (int c = 0; c < headers.Length; c++)
                        ws.Cell(1, c + 1).Value = headers[c];

                    var rows = _rows.OrderBy(r => r.SortRank).ToList();
                    for (int r = 0; r < rows.Count; r++)
                    {
                        var it = rows[r];
                        ws.Cell(r + 2, 1).Value = it.Hostname;
                        ws.Cell(r + 2, 2).Value = it.Alias ?? string.Empty;
                        ws.Cell(r + 2, 3).Value = it.CurrentStatus;
                        ws.Cell(r + 2, 4).Value = it.Classification;
                        ws.Cell(r + 2, 5).Value = it.Sent;
                        ws.Cell(r + 2, 6).Value = it.Received;
                        ws.Cell(r + 2, 7).Value = it.Lost;
                        ws.Cell(r + 2, 8).Value = Math.Round(it.LossPct, 2);
                        ws.Cell(r + 2, 9).Value = it.MinRtt;
                        ws.Cell(r + 2, 10).Value = Math.Round(it.AvgRtt, 2);
                        ws.Cell(r + 2, 11).Value = it.MaxRtt;

                        var clsCell = ws.Cell(r + 2, 4);
                        if (it.Classification == "Bien")
                            clsCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1A10B981");
                        else if (it.Classification == "Intermitente")
                            clsCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#26F59E0B");
                        else if (it.Classification == "Fallando")
                            clsCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#26EF4444");
                    }
                    ws.Columns().AdjustToContents();

                    // Sheet 2: Historial (cada muestra con hora y ms)
                    var ws2 = wb.Worksheets.Add("Historial");
                    string[] h2 = { "Hostname", "Alias", "Fecha y hora", "Estado", "RTT (ms)" };
                    for (int c = 0; c < h2.Length; c++)
                        ws2.Cell(1, c + 1).Value = h2[c];

                    DateTime windowStart = GetWindowStart();
                    int rowIdx = 2;
                    foreach (var probe in _probes)
                    {
                        var samples = probe.PingSamples.Where(s => s.Timestamp >= windowStart).OrderBy(s => s.Timestamp).ToList();
                        foreach (var s in samples)
                        {
                            ws2.Cell(rowIdx, 1).Value = probe.Hostname;
                            ws2.Cell(rowIdx, 2).Value = probe.Alias ?? string.Empty;
                            ws2.Cell(rowIdx, 3).Value = s.Timestamp;
                            ws2.Cell(rowIdx, 4).Value = s.Success ? "OK" : "Perdida";
                            if (s.Success)
                                ws2.Cell(rowIdx, 5).Value = s.RttMs;
                            rowIdx++;
                        }
                    }
                    ws2.Columns().AdjustToContents();

                    wb.SaveAs(dlg.FileName);
                    lblStatus.Text = $"Exportado a Excel: {Path.GetFileName(dlg.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar a Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportHtml_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Guardar reporte (HTML)",
                Filter = "Archivo HTML (*.html)|*.html",
                DefaultExt = "html",
                FileName = $"vmPing_Reporte_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
                sb.AppendLine("<title>Reporte vmPing GLR</title>");
                sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2937}h1{font-size:22px}table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #d1d5db;padding:6px 8px;text-align:left}th{background:#f3f4f6}.bien{background:#d1fae5}.inter{background:#fef3c7}.fall{background:#fee2e2}</style></head><body>");
                sb.AppendLine($"<h1>Reporte de Monitoreo vmPing</h1><p>Generado: {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Hosts: {_rows.Count}</p>");
                sb.AppendLine("<table><thead><tr><th>Hostname</th><th>Alias</th><th>Estado</th><th>Clasificación</th><th>Enviados</th><th>Recibidos</th><th>Perdidos</th><th>% Pérdida</th><th>Min RTT</th><th>Prom RTT</th><th>Max RTT</th></tr></thead><tbody>");

                foreach (var it in _rows.OrderBy(r => r.SortRank))
                {
                    string cls = it.Classification == "Bien" ? "bien" : it.Classification == "Intermitente" ? "inter" : it.Classification == "Fallando" ? "fall" : "";
                    sb.AppendLine($"<tr class='{cls}'><td>{System.Net.WebUtility.HtmlEncode(it.Hostname)}</td><td>{System.Net.WebUtility.HtmlEncode(it.Alias)}</td><td>{it.CurrentStatus}</td><td>{it.Classification}</td><td>{it.Sent}</td><td>{it.Received}</td><td>{it.Lost}</td><td>{it.LossPct:F2}</td><td>{it.MinRtt}</td><td>{it.AvgRtt:F2}</td><td>{it.MaxRtt}</td></tr>");
                }

                sb.AppendLine("</tbody></table></body></html>");
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                lblStatus.Text = $"Exportado a HTML: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar a HTML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class ExportRow
    {
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public string CurrentStatus { get; set; }
        public string Classification { get; set; }
        public int Sent { get; set; }
        public int Received { get; set; }
        public int Lost { get; set; }
        public double LossPct { get; set; }
        public int MinRtt { get; set; }
        public double AvgRtt { get; set; }
        public int MaxRtt { get; set; }
        public int SortRank { get; set; }
    }
}
