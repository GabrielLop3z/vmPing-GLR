using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class InventoryWindow : Window
    {
        private readonly List<string> _hosts;
        private List<DeviceInventory> _devices = new List<DeviceInventory>();
        private CancellationTokenSource _cancellation;

        public InventoryWindow(List<string> hosts)
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _hosts = hosts ?? new List<string>();
            lblSubtitle.Text = $"{_hosts.Count} equipo(s) seleccionados. Fuentes: WMI + SNMP.";

            if (_hosts.Count == 0)
            {
                lblStatus.Text = "No hay equipos activos para inventariar.";
                btnRefresh.IsEnabled = false;
                return;
            }

            _ = CollectAsync();
        }

        private async System.Threading.Tasks.Task CollectAsync()
        {
            btnRefresh.IsEnabled = false;
            btnCancel.Visibility = Visibility.Visible;
            btnExportExcel.IsEnabled = false;
            btnExportHtml.IsEnabled = false;

            _cancellation?.Cancel();
            _cancellation = new CancellationTokenSource();

            var progress = new Progress<InventoryProgress>(p =>
            {
                if (p.Total > 0)
                {
                    progressBar.Value = (double)p.Completed / p.Total * 100;
                    lblProgress.Text = $"{p.Completed} / {p.Total}";
                }
                lblStatus.Text = $"{p.CurrentHost} - {p.Message}";
            });

            var devices = await InventoryService.CollectAsync(_hosts, progress, _cancellation.Token);
            _devices = devices;

            btnRefresh.IsEnabled = true;
            btnCancel.Visibility = Visibility.Collapsed;
            btnExportExcel.IsEnabled = true;
            btnExportHtml.IsEnabled = true;
            progressBar.Value = 100;
            lblProgress.Text = $"{devices.Count} / {_hosts.Count}";
            lblSubtitle.Text = $"{_hosts.Count} equipo(s). Disponible: {devices.Count(d => d.IsReachable)} - Sin datos: {devices.Count(d => !d.IsReachable)}";

            dgInventory.ItemsSource = devices;
            InventoryStore.Save(devices);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _ = CollectAsync();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cancellation?.Cancel();
            lblStatus.Text = "Cancelando recolección...";
        }

        private void DgInventory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgInventory.SelectedItem is DeviceInventory device)
            {
                OpenDetail(device);
            }
        }

        private void OpenDetail(DeviceInventory device)
        {
            var window = new DeviceInfoWindow(device)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Guardar inventario de Excel",
                Filter = "Archivo Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                FileName = $"Inventario_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Inventario");

                    string[] headers = { "Host", "Alias", "Fuente", "Estado", "Fabricante", "Modelo", "No. Serie", "UUID", "Sistema Operativo", "Versión", "Arquitectura", "CPU", "Cores", "RAM (GB)", "Dominio", "DNS Hostname", "IP", "MAC", "Último arranque", "Recolectado (UTC)" };
                    for (int c = 0; c < headers.Length; c++)
                        ws.Cell(1, c + 1).Value = headers[c];

                    for (int r = 0; r < _devices.Count; r++)
                    {
                        var d = _devices[r];
                        ws.Cell(r + 2, 1).Value = d.Host;
                        ws.Cell(r + 2, 2).Value = d.Alias ?? string.Empty;
                        ws.Cell(r + 2, 3).Value = d.Source ?? string.Empty;
                        ws.Cell(r + 2, 4).Value = d.IsReachableText;
                        ws.Cell(r + 2, 5).Value = d.Manufacturer ?? string.Empty;
                        ws.Cell(r + 2, 6).Value = d.Model ?? string.Empty;
                        ws.Cell(r + 2, 7).Value = d.SerialNumber ?? string.Empty;
                        ws.Cell(r + 2, 8).Value = d.Uuid ?? string.Empty;
                        ws.Cell(r + 2, 9).Value = d.OsCaption ?? string.Empty;
                        ws.Cell(r + 2, 10).Value = d.OsVersion ?? string.Empty;
                        ws.Cell(r + 2, 11).Value = d.OsArchitecture ?? string.Empty;
                        ws.Cell(r + 2, 12).Value = d.CpuName ?? string.Empty;
                        ws.Cell(r + 2, 13).Value = d.CpuCores;
                        ws.Cell(r + 2, 14).Value = d.TotalRamGB;
                        ws.Cell(r + 2, 15).Value = d.Domain ?? string.Empty;
                        ws.Cell(r + 2, 16).Value = d.DnsHostname ?? string.Empty;
                        ws.Cell(r + 2, 17).Value = d.Ipv4 ?? string.Empty;
                        ws.Cell(r + 2, 18).Value = d.MacAddresses ?? string.Empty;
                        ws.Cell(r + 2, 19).Value = d.LastBootUpTime ?? string.Empty;
                        ws.Cell(r + 2, 20).Value = d.CollectedUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                    }

                    ws.Columns().AdjustToContents();
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
                Title = "Guardar inventario HTML",
                Filter = "Archivo HTML (*.html)|*.html",
                DefaultExt = "html",
                FileName = $"Inventario_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
                sb.AppendLine("<title>Inventario de Equipos - vmPing GLR</title>");
                sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2937}h1{font-size:22px}table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #d1d5db;padding:6px 8px;text-align:left}th{background:#f3f4f6;position:sticky;top:0}tr:nth-child(even){background:#f9fafb}.ok{color:#047857;font-weight:bold}.nook{color:#b91c1c}</style></head><body>");
                sb.AppendLine($"<h1>Inventario de Equipos</h1><p>Generado: {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Total: {_devices.Count}</p>");
                sb.AppendLine("<table><thead><tr><th>Host</th><th>Alias</th><th>Fuente</th><th>Estado</th><th>Fabricante</th><th>Modelo</th><th>No. Serie</th><th>SO</th><th>CPU</th><th>RAM (GB)</th><th>IP</th><th>MAC</th></tr></thead><tbody>");

                foreach (var d in _devices)
                {
                    var state = d.IsReachable ? "<span class='ok'>Disponible</span>" : "<span class='nook'>Sin datos</span>";
                    sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(d.Host)}</td><td>{System.Net.WebUtility.HtmlEncode(d.Alias)}</td><td>{System.Net.WebUtility.HtmlEncode(d.Source)}</td><td>{state}</td><td>{System.Net.WebUtility.HtmlEncode(d.Manufacturer)}</td><td>{System.Net.WebUtility.HtmlEncode(d.Model)}</td><td>{System.Net.WebUtility.HtmlEncode(d.SerialNumber)}</td><td>{System.Net.WebUtility.HtmlEncode(d.OsCaption)}</td><td>{System.Net.WebUtility.HtmlEncode(d.CpuName)}</td><td>{d.TotalRamGB}</td><td>{System.Net.WebUtility.HtmlEncode(d.Ipv4)}</td><td>{System.Net.WebUtility.HtmlEncode(d.MacAddresses)}</td></tr>");
                }

                sb.AppendLine("</tbody></table></body></html>");
                File.WriteAllText(dlg.FileName, sb.ToString());
                lblStatus.Text = $"Exportado a HTML: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar a HTML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
