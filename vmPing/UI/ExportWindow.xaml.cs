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
            if (rb30Min != null && rb30Min.IsChecked == true)
                return DateTime.Now.AddMinutes(-30);
            return DateTime.MinValue;
        }

        private void RefreshRows()
        {
            if (dgExport == null || _probes == null)
                return;

            _rows.Clear();

            DateTime windowStart = GetWindowStart();

            foreach (var probe in _probes)
            {
                var samples = probe.PingSamples.Where(s => s.Timestamp >= windowStart).ToList();
                var row = BuildRow(probe, samples, windowStart);
                _rows.Add(row);
            }

            // Ordenar: Fallando → Intermitente → Bien → Sin datos
            dgExport.ItemsSource = _rows.OrderBy(r => r.SortRank).ToList();
            lblSubtitle.Text = rb30Min != null && rb30Min.IsChecked == true
                ? $"Historial de conexión (últimos 30 min). {_rows.Count} hosts."
                : $"Historial de conexión completo de la sesión. {_rows.Count} hosts.";
        }

        private ExportRow BuildRow(Probe probe, List<PingSample> samples, DateTime windowStart)
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
                double variance = ok.Average(v => (v - row.AvgRtt) * (v - row.AvgRtt));
                row.StdDev = Math.Sqrt(variance);
            }

            ComputeDownTime(probe, windowStart, out int downEvents, out TimeSpan downTotal);
            row.DownEvents = downEvents;
            row.DownTimeTotal = downTotal;

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

        // Calcula el número de eventos de caída y el tiempo total caído dentro de la ventana,
        // usando el historial de cambios de estado del probe (Down → Up / ahora).
        private static void ComputeDownTime(Probe probe, DateTime windowStart, out int downEvents, out TimeSpan downTotal)
        {
            downEvents = 0;
            downTotal = TimeSpan.Zero;

            var changes = Probe.StatusChangeLog
                .Where(c => c.Hostname == probe.Hostname && c.Timestamp >= windowStart)
                .OrderBy(c => c.Timestamp)
                .ToList();

            DateTime? downSince = null;
            foreach (var change in changes)
            {
                if (change.Status == ProbeStatus.Down && !downSince.HasValue)
                {
                    downSince = change.Timestamp;
                }
                else if ((change.Status == ProbeStatus.Up || change.Status == ProbeStatus.Stop) && downSince.HasValue)
                {
                    downTotal += change.Timestamp - downSince.Value;
                    downEvents++;
                    downSince = null;
                }
            }
            if (downSince.HasValue)
            {
                downTotal += DateTime.Now - downSince.Value;
                downEvents++;
            }
        }

        private static string FormatDownTime(TimeSpan t)
        {
            if (t <= TimeSpan.Zero)
                return "0";
            return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        }

        // Replica el texto que vmPing muestra en la consola para cada muestra        // (mismo formato que Probe-Icmp.DisplayIcmpReply y Probe-Tcp.DisplayTcpReply):
        //   "[09:28:25 a. m.]  Respuesta de 172.25.39.126  [100 ms]"
        //   "[09:28:25 a. m.]  Tiempo de espera agotado."
        //   "[09:28:25 a. m.]  Puerto 80: Conectado  [12 ms]"
        private string BuildConsoleLine(Probe probe, PingSample s)
        {
            string line = s.Timestamp.ToLongTimeString();

            // TCP probe: el hostname es "host:puerto" (ver Probe.IsTcpPing).
            if (probe.Hostname.Count(f => f == ':') == 1 || probe.Hostname.Contains("]:"))
            {
                var host = probe.Hostname.Substring(0, probe.Hostname.LastIndexOf(':')).Trim('[', ']');
                var port = probe.Hostname.Substring(probe.Hostname.LastIndexOf(':') + 1);
                line += "  Puerto " + port + ": ";
                line += s.Success
                    ? "Conectado  [" + s.RttMs + " ms]"
                    : "Cerrado";
                return "[" + line + "]";
            }

            // ICMP probe.
            line += s.Success
                ? "  Respuesta de " + probe.Hostname + (s.RttMs < 1 ? "  [<1ms]" : "  [" + s.RttMs + " ms]")
                : "  Tiempo de espera agotado.";
            return "[" + line + "]";
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

                    string[] headers = { "Hostname", "Alias", "Estado", "Clasificación", "Enviados", "Recibidos", "Perdidos", "% Pérdida", "Min RTT (ms)", "Prom RTT (ms)", "Max RTT (ms)", "Desv. RTT (ms)", "Caídas", "Tiempo caído (min)" };
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
                        ws.Cell(r + 2, 12).Value = Math.Round(it.StdDev, 2);
                        ws.Cell(r + 2, 13).Value = it.DownEvents;
                        ws.Cell(r + 2, 14).Value = Math.Round(it.DownTimeTotal.TotalMinutes, 2);

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
                    string[] h2 = { "Hostname", "Alias", "Fecha y hora", "Estado", "RTT (ms)", "Salida" };
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
                            ws2.Cell(rowIdx, 6).Value = BuildConsoleLine(probe, s);
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
                int totalUp = _rows.Count(r => r.Classification == "Bien");
                int totalDown = _rows.Count(r => r.Classification == "Fallando");
                int totalInter = _rows.Count(r => r.Classification == "Intermitente");
                int totalNoData = _rows.Count(r => r.Classification == "Sin datos");
                double avgLoss = _rows.Any() ? _rows.Average(r => r.LossPct) : 0;
                double avgRttAll = _rows.Where(r => r.AvgRtt > 0).Any() ? _rows.Where(r => r.AvgRtt > 0).Average(r => r.AvgRtt) : 0;

                sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
                sb.AppendLine("<title>Dashboard vmPing GLR</title>");
                sb.AppendLine("<style>");
                sb.AppendLine("*{margin:0;padding:0;box-sizing:border-box}");
                sb.AppendLine("body{font-family:'Segoe UI',system-ui,-apple-system,sans-serif;background:#f1f5f9;color:#1e293b;line-height:1.5}");
                sb.AppendLine(".header{background:linear-gradient(135deg,#1e3a8a 0%,#2563eb 100%);color:#fff;padding:28px 32px;display:flex;justify-content:space-between;align-items:center}");
                sb.AppendLine(".header h1{font-size:22px;font-weight:600;letter-spacing:-0.3px}");
                sb.AppendLine(".header .meta{font-size:13px;opacity:0.85;text-align:right}");
                sb.AppendLine(".header .meta span{display:block}");
                sb.AppendLine(".container{max-width:1400px;margin:0 auto;padding:24px 28px}");
                sb.AppendLine(".cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:16px;margin-bottom:28px}");
                sb.AppendLine(".card{background:#fff;border-radius:12px;padding:20px 22px;box-shadow:0 1px 3px rgba(0,0,0,0.06);border-left:4px solid #e2e8f0;transition:transform 0.15s}");
                sb.AppendLine(".card:hover{transform:translateY(-2px)}");
                sb.AppendLine(".card.up{border-left-color:#10b981}.card.down{border-left-color:#ef4444}.card.inter{border-left-color:#f59e0b}.card.nodata{border-left-color:#94a3b8}.card.total{border-left-color:#2563eb}.card.loss{border-left-color:#f97316}.card.rtt{border-left-color:#6366f1}");
                sb.AppendLine(".card .label{font-size:12px;color:#64748b;text-transform:uppercase;letter-spacing:0.5px;font-weight:500}");
                sb.AppendLine(".card .value{font-size:28px;font-weight:700;margin-top:4px}");
                sb.AppendLine(".card.total .value{color:#2563eb}.card.up .value{color:#10b981}.card.down .value{color:#ef4444}.card.inter .value{color:#f59e0b}.card.nodata .value{color:#94a3b8}.card.loss .value{color:#f97316}.card.rtt .value{color:#6366f1}");
                sb.AppendLine(".section{background:#fff;border-radius:12px;box-shadow:0 1px 3px rgba(0,0,0,0.06);margin-bottom:24px;overflow:hidden}");
                sb.AppendLine(".section-title{font-size:16px;font-weight:600;padding:18px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:center;gap:8px}");
                sb.AppendLine(".section-title .icon{width:8px;height:8px;border-radius:50%;display:inline-block}");
                sb.AppendLine(".section-title .icon.blue{background:#2563eb}.section-title .icon.green{background:#10b981}.section-title .icon.amber{background:#f59e0b}.section-title .icon.red{background:#ef4444}");
                sb.AppendLine("table{width:100%;border-collapse:collapse;font-size:13px}");
                sb.AppendLine("thead th{background:#f8fafc;color:#475569;font-weight:600;text-align:left;padding:12px 14px;border-bottom:2px solid #e2e8f0;white-space:nowrap;position:sticky;top:0}");
                sb.AppendLine("tbody td{padding:10px 14px;border-bottom:1px solid #f1f5f9;vertical-align:middle}");
                sb.AppendLine("tbody tr:hover{background:#f8fafc}");
                sb.AppendLine("tbody tr:last-child td{border-bottom:none}");
                sb.AppendLine(".badge{display:inline-block;padding:3px 10px;border-radius:20px;font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:0.3px}");
                sb.AppendLine(".badge.up{background:#d1fae5;color:#065f46}.badge.down{background:#fee2e2;color:#991b1b}.badge.inter{background:#fef3c7;color:#92400e}.badge.nodata{background:#f1f5f9;color:#64748b}");
                sb.AppendLine(".rtt-bar{display:inline-block;height:6px;border-radius:3px;vertical-align:middle;margin-right:6px}");
                sb.AppendLine(".loss-high{color:#ef4444;font-weight:600}.loss-mid{color:#f59e0b;font-weight:600}.loss-ok{color:#10b981}");
                sb.AppendLine(".host-block{margin-bottom:24px;border:1px solid #e2e8f0;border-radius:10px;overflow:hidden}");
                sb.AppendLine(".host-block:last-child{margin-bottom:0}");
                sb.AppendLine(".host-header{padding:14px 18px;background:#f8fafc;border-bottom:1px solid #e2e8f0;display:flex;justify-content:space-between;align-items:center}");
                sb.AppendLine(".host-header h3{font-size:14px;font-weight:600}");
                sb.AppendLine(".host-header .count{font-size:12px;color:#64748b}");
                sb.AppendLine(".host-table{width:100%;font-size:12px}");
                sb.AppendLine(".host-table th{background:#fff;padding:8px 14px;font-weight:600;color:#64748b;text-transform:uppercase;font-size:11px;letter-spacing:0.3px}");
                sb.AppendLine(".host-table td{padding:7px 14px}");
                sb.AppendLine(".host-table tbody tr:nth-child(even){background:#fafbfc}");
                sb.AppendLine(".footer{text-align:center;padding:20px;color:#94a3b8;font-size:12px}");
                sb.AppendLine("@media print{body{background:#fff}.cards{gap:8px}.section{box-shadow:none;border:1px solid #e2e8f0}thead th{background:#f1f5f9}}");
                sb.AppendLine("</style></head><body>");

                sb.AppendLine("<div class='header'><div><h1>Dashboard de Monitoreo vmPing</h1></div><div class='meta'>");
                sb.AppendLine($"<span>Generado: {DateTime.Now:dddd dd 'de' MMMM yyyy, HH:mm:ss}</span>");
                sb.AppendLine($"<span>Hosts monitoreados: {_rows.Count}</span>");
                sb.AppendLine("</div></div>");

                sb.AppendLine("<div class='container'>");

                sb.AppendLine("<div class='cards'>");
                sb.AppendLine($"<div class='card total'><div class='label'>Total Hosts</div><div class='value'>{_rows.Count}</div></div>");
                sb.AppendLine($"<div class='card up'><div class='label'>En línea</div><div class='value'>{totalUp}</div></div>");
                sb.AppendLine($"<div class='card down'><div class='label'>Caídos</div><div class='value'>{totalDown}</div></div>");
                sb.AppendLine($"<div class='card inter'><div class='label'>Intermitentes</div><div class='value'>{totalInter}</div></div>");
                if (totalNoData > 0)
                    sb.AppendLine($"<div class='card nodata'><div class='label'>Sin datos</div><div class='value'>{totalNoData}</div></div>");
                sb.AppendLine($"<div class='card loss'><div class='label'>Pérdida Promedio</div><div class='value'>{avgLoss:F1}%</div></div>");
                sb.AppendLine($"<div class='card rtt'><div class='label'>RTT Promedio</div><div class='value'>{avgRttAll:F0} ms</div></div>");
                sb.AppendLine("</div>");

                sb.AppendLine("<div class='section'><div class='section-title'><span class='icon blue'></span> Resumen por Host</div>");
                sb.AppendLine("<div style='overflow-x:auto'><table><thead><tr><th>Hostname</th><th>Alias</th><th>Estado</th><th>Clasificación</th><th>Enviados</th><th>Recibidos</th><th>Perdidos</th><th>% Pérdida</th><th>RTT Mín</th><th>RTT Prom</th><th>RTT Máx</th><th>Desv. Est.</th><th>Caídas</th><th>Tiempo Caído</th></tr></thead><tbody>");

                foreach (var it in _rows.OrderBy(r => r.SortRank))
                {
                    string badgeCls = it.Classification == "Bien" ? "up" : it.Classification == "Intermitente" ? "inter" : it.Classification == "Fallando" ? "down" : "nodata";
                    string lossCls = it.LossPct >= 10 ? "loss-high" : it.LossPct > 0 ? "loss-mid" : "loss-ok";
                    string lossBar = it.LossPct > 0 ? $"<div class='rtt-bar' style='width:{Math.Min(it.LossPct, 100) * 0.6}px;background:{(it.LossPct >= 10 ? "#ef4444" : "#f59e0b")}'></div>" : "";
                    sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(it.Hostname)}</td><td>{System.Net.WebUtility.HtmlEncode(it.Alias)}</td><td>{it.CurrentStatus}</td><td><span class='badge {badgeCls}'>{it.Classification}</span></td><td>{it.Sent}</td><td>{it.Received}</td><td>{it.Lost}</td><td>{lossBar}<span class='{lossCls}'>{it.LossPct:F2}%</span></td><td>{it.MinRtt}</td><td>{it.AvgRtt:F2}</td><td>{it.MaxRtt}</td><td>{it.StdDev:F2}</td><td>{it.DownEvents}</td><td>{FormatDownTime(it.DownTimeTotal)}</td></tr>");
                }

                sb.AppendLine("</tbody></table></div></div>");

                DateTime windowStart = GetWindowStart();
                sb.AppendLine("<div class='section'><div class='section-title'><span class='icon green'></span> Historial por Host</div>");
                sb.AppendLine("<div style='padding:16px 18px'>");

                foreach (var probe in _probes)
                {
                    var samples = probe.PingSamples
                        .Where(s => s.Timestamp >= windowStart)
                        .OrderBy(s => s.Timestamp)
                        .ToList();
                    if (samples.Count == 0)
                        continue;

                    int upCount = samples.Count(s => s.Success);
                    int downCount = samples.Count(s => !s.Success);
                    string hostAlias = string.IsNullOrWhiteSpace(probe.Alias) ? "" : $" ({System.Net.WebUtility.HtmlEncode(probe.Alias)})";

                    sb.AppendLine($"<div class='host-block'>");
                    sb.AppendLine($"<div class='host-header'><h3>{System.Net.WebUtility.HtmlEncode(probe.Hostname)}{hostAlias}</h3><div class='count'>{samples.Count} muestras &middot; <span style='color:#10b981'>{upCount} OK</span> &middot; <span style='color:#ef4444'>{downCount} fallas</span></div></div>");
                    sb.AppendLine("<table class='host-table'><thead><tr><th>Fecha y hora</th><th>Estado</th><th>RTT (ms)</th><th>Salida</th></tr></thead><tbody>");

                    foreach (var s in samples)
                    {
                        string st = s.Success ? "<span class='badge up'>OK</span>" : "<span class='badge down'>Falla</span>";
                        sb.AppendLine($"<tr><td>{s.Timestamp:yyyy-MM-dd HH:mm:ss}</td><td>{st}</td><td>{(s.Success ? s.RttMs.ToString() : "-")}</td><td>{System.Net.WebUtility.HtmlEncode(BuildConsoleLine(probe, s))}</td></tr>");
                    }
                    sb.AppendLine("</tbody></table></div>");
                }

                sb.AppendLine("</div></div>");
                sb.AppendLine($"<div class='footer'>vmPing GLR &mdash; Reporte generado el {DateTime.Now:yyyy-MM-dd 'a las' HH:mm:ss}</div>");
                sb.AppendLine("</div></body></html>");
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
        public double StdDev { get; set; }
        public int DownEvents { get; set; }
        public TimeSpan DownTimeTotal { get; set; }
        public int SortRank { get; set; }
    }
}
