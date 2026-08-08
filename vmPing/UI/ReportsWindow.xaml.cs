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
    public partial class ReportsWindow : Window
    {
        private readonly ObservableCollection<Probe> _probes;

        public ReportsWindow(ObservableCollection<Probe> probes)
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _probes = probes ?? new ObservableCollection<Probe>();
            dgProbes.ItemsSource = _probes.Select(p => new ProbeRow
            {
                Hostname = p.Hostname,
                Alias = p.Alias,
                Status = p.Status.ToString(),
                Sent = p.Statistics?.Sent ?? 0,
                Received = p.Statistics?.Received ?? 0,
                Lost = p.Statistics?.Lost ?? 0,
                Error = p.Statistics?.Error ?? 0,
                MinRtt = p.Statistics?.MinRtt ?? 0,
                AvgRtt = p.Statistics?.AvgRtt ?? 0,
                MaxRtt = p.Statistics?.MaxRtt ?? 0
            }).ToList();

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            var rows = dgProbes.ItemsSource as IEnumerable<object>;
            int total = dgProbes.Items.Count;
            int up = dgProbes.Items.Cast<ProbeRow>().Count(r => r.Status == "Up" || r.Sent > 0 && r.Lost == 0);
            int down = dgProbes.Items.Cast<ProbeRow>().Count(r => r.Status == "Down");
            double avgLatency = dgProbes.Items.Cast<ProbeRow>().Where(r => r.AvgRtt > 0).Select(r => r.AvgRtt).DefaultIfEmpty(0).Average();

            lblTotal.Text = total.ToString();
            lblUp.Text = up.ToString();
            lblDown.Text = down.ToString();
            lblAvgLatency.Text = $"{avgLatency:F1} ms";
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            ExportToExcel();
        }

        private void BtnExportHtml_Click(object sender, RoutedEventArgs e)
        {
            ExportToHtml();
        }

        private string PromptSavePath(string title, string filter, string defaultExt)
        {
            var dlg = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = $"vmPing_Report_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void ExportToExcel()
        {
            string path = PromptSavePath("Guardar reporte de Excel", "Archivo Excel (*.xlsx)|*.xlsx", "xlsx");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Hosts");

                    // Header row
                    string[] headers = { "Hostname", "Alias", "Estado", "Enviados", "Recibidos", "Perdidos", "Error", "Min RTT (ms)", "Prom RTT (ms)", "Max RTT (ms)" };
                    for (int c = 0; c < headers.Length; c++)
                        ws.Cell(1, c + 1).Value = headers[c];

                    var rows = dgProbes.Items.Cast<ProbeRow>().ToList();
                    for (int r = 0; r < rows.Count; r++)
                    {
                        ws.Cell(r + 2, 1).Value = rows[r].Hostname;
                        ws.Cell(r + 2, 2).Value = rows[r].Alias;
                        ws.Cell(r + 2, 3).Value = rows[r].Status;
                        ws.Cell(r + 2, 4).Value = rows[r].Sent;
                        ws.Cell(r + 2, 5).Value = rows[r].Received;
                        ws.Cell(r + 2, 6).Value = rows[r].Lost;
                        ws.Cell(r + 2, 7).Value = rows[r].Error;
                        ws.Cell(r + 2, 8).Value = rows[r].MinRtt;
                        ws.Cell(r + 2, 9).Value = rows[r].AvgRtt;
                        ws.Cell(r + 2, 10).Value = rows[r].MaxRtt;
                    }

                    // Auto width
                    ws.Columns().AdjustToContents();

                    // Summary sheet
                    var ws2 = wb.Worksheets.Add("Resumen");
                    int rowIdx = 1;
                    ws2.Cell(rowIdx++, 1).Value = "vmPing GLR - Reporte de Estado";
                    ws2.Cell(rowIdx++, 1).Value = $"Generado: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    ws2.Cell(rowIdx++, 1).Value = $"Total de Hosts: {_probes.Count}";
                    rowIdx++;

                    foreach (var p in _probes)
                    {
                        ws2.Cell(rowIdx++, 1).Value = p.Hostname;
                        ws2.Cell(rowIdx - 1, 2).Value = p.Statistics?.Sent ?? 0;
                        ws2.Cell(rowIdx - 1, 3).Value = p.Statistics?.Received ?? 0;
                        ws2.Cell(rowIdx - 1, 4).Value = p.Status.ToString();
                    }

                    wb.SaveAs(path);
                    lblStatus.Text = $"Exportado a Excel: {Path.GetFileName(path)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar a Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToHtml()
        {
            string path = PromptSavePath("Guardar reporte HTML", "Archivo HTML (*.html)|*.html", "html");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html><head>");
                sb.AppendLine("<meta charset='utf-8'>");
                sb.AppendLine("<title>Reporte vmPing GLR</title>");
                sb.AppendLine("<style>");
                sb.AppendLine("body { font-family: 'Segoe UI', Arial; margin: 20px; background: #f8fafc; color: #1e293b; }");
                sb.AppendLine("h1 { color: #0ea5e9; }");
                sb.AppendLine("table { border-collapse: collapse; width: 100%; background: #fff; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }");
                sb.AppendLine("th, td { border: 1px solid #dbe2eb; padding: 8px 12px; text-align: left; }");
                sb.AppendLine("th { background: #0ea5e9; color: #fff; }");
                sb.AppendLine("tr:nth-child(even) { background: #f1f5f9; }");
                sb.AppendLine("</style></head><body>");
                sb.AppendLine($"<h1>Reporte vmPing GLR</h1>");
                sb.AppendLine($"<p>Generado: <strong>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</strong></p>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Hostname</th><th>Alias</th><th>Estado</th><th>Enviados</th><th>Recibidos</th><th>Perdidos</th><th>Error</th><th>Min RTT</th><th>Prom RTT</th><th>Max RTT</th></tr>");

                foreach (var r in dgProbes.Items.Cast<ProbeRow>())
                {
                    sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(r.Hostname)}</td><td>{System.Net.WebUtility.HtmlEncode(r.Alias)}</td><td>{r.Status}</td><td>{r.Sent:N0}</td><td>{r.Received:N0}</td><td>{r.Lost:N0}</td><td>{r.Error:N0}</td><td>{r.MinRtt:N0}</td><td>{r.AvgRtt:N2}</td><td>{r.MaxRtt:N0}</td></tr>");
                }

                sb.AppendLine("</table></body></html>");

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                lblStatus.Text = $"Exportado a HTML: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar a HTML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class ProbeRow
    {
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public string Status { get; set; }
        public uint Sent { get; set; }
        public uint Received { get; set; }
        public uint Lost { get; set; }
        public uint Error { get; set; }
        public long MinRtt { get; set; }
        public double AvgRtt { get; set; }
        public long MaxRtt { get; set; }
    }
}
