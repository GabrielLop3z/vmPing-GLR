using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class AdWindow : Window
    {
        private List<AdComputer> _computers = new List<AdComputer>();
        private List<AdUser> _users = new List<AdUser>();
        private List<DeviceInventory> _inventory = new List<DeviceInventory>();
        private bool _loaded;

        public AdWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            lblSubtitle.Text = $"Dominio: {Environment.UserDomainName}. Ruta LDAP: {AdService.GetLdapPath()}";
            _ = LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            btnRefresh.IsEnabled = false;
            lblStatus.Text = "Consultando Active Directory...";

            _inventory = AdService.LoadInventoryFromDisk();

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    _computers = AdService.QueryComputers();
                    AdService.CorrelateWithInventory(_computers, _inventory);
                    _users = AdService.QueryEnabledUsers();
                }
                catch (Exception ex)
                {
                    _computers = new List<AdComputer>();
                    _users = new List<AdUser>();
                    lblStatus.Dispatcher.Invoke(() =>
                    {
                        lblStatus.Text = $"Error al consultar Active Directory: {ex.Message}";
                    });
                    return;
                }
            });

            _loaded = true;
            btnRefresh.IsEnabled = true;
            ApplyFilter();
            dgUsers.ItemsSource = _users;
            lblSubtitle.Text = $"Dominio: {Environment.UserDomainName}. {_computers.Count} computadoras, {_users.Count} usuarios. Inventario correlacionado: {_inventory.Count} equipos.";
            lblStatus.Text = $"Datos cargados. {_computers.Count(c => c.HasInventory)} de {_computers.Count} computadoras tienen inventario WMI.";
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadAsync();
        }

        private void TxtFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_loaded)
            {
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            var filter = txtFilter.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            var list = _computers
                .Where(c => string.IsNullOrEmpty(filter)
                    || (c.Name?.ToLowerInvariant().Contains(filter) ?? false)
                    || (c.DnsHostName?.ToLowerInvariant().Contains(filter) ?? false)
                    || (c.OuPath?.ToLowerInvariant().Contains(filter) ?? false)
                    || (c.Description?.ToLowerInvariant().Contains(filter) ?? false)
                    || (c.OperatingSystem?.ToLowerInvariant().Contains(filter) ?? false))
                .ToList();
            dgComputers.ItemsSource = list;
            lblCount.Text = $"{list.Count} / {_computers.Count}";
        }

        private void DgComputers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dgComputers.SelectedItem is AdComputer computer && computer.HasInventory)
            {
                var window = new DeviceInfoWindow(computer.Inventory)
                {
                    Owner = this
                };
                window.ShowDialog();
            }
        }

        private string PromptSavePath(string title, string filter, string defaultExt)
        {
            var dlg = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = $"AD_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            string path = PromptSavePath("Guardar Active Directory en Excel", "Archivo Excel (*.xlsx)|*.xlsx", "xlsx");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Computadoras");
                    string[] headers = { "Equipo", "DNS", "Sistema Operativo", "OU", "Descripción", "Estado", "Último Logon", "Creado", "Inventario", "Serie", "Modelo", "RAM", "IP", "MAC" };
                    for (int c = 0; c < headers.Length; c++)
                        ws.Cell(1, c + 1).Value = headers[c];

                    for (int r = 0; r < _computers.Count; r++)
                    {
                        var c = _computers[r];
                        ws.Cell(r + 2, 1).Value = c.Name;
                        ws.Cell(r + 2, 2).Value = c.DnsHostName ?? string.Empty;
                        ws.Cell(r + 2, 3).Value = c.OperatingSystem ?? string.Empty;
                        ws.Cell(r + 2, 4).Value = c.OuPath ?? string.Empty;
                        ws.Cell(r + 2, 5).Value = c.Description ?? string.Empty;
                        ws.Cell(r + 2, 6).Value = c.StatusText;
                        ws.Cell(r + 2, 7).Value = c.LastLogonText;
                        ws.Cell(r + 2, 8).Value = c.WhenCreatedText;
                        ws.Cell(r + 2, 9).Value = c.InventoryStateText;
                        ws.Cell(r + 2, 10).Value = c.Serials;
                        ws.Cell(r + 2, 11).Value = c.Model;
                        ws.Cell(r + 2, 12).Value = c.RamText;
                        ws.Cell(r + 2, 13).Value = c.IpText;
                        ws.Cell(r + 2, 14).Value = c.MacText;
                    }

                    var ws2 = wb.Worksheets.Add("Usuarios");
                    string[] userHeaders = { "Nombre", "Usuario", "OU", "Descripción", "Estado", "Último Logon" };
                    for (int c = 0; c < userHeaders.Length; c++)
                        ws2.Cell(1, c + 1).Value = userHeaders[c];
                    for (int r = 0; r < _users.Count; r++)
                    {
                        var u = _users[r];
                        ws2.Cell(r + 2, 1).Value = u.DisplayName ?? string.Empty;
                        ws2.Cell(r + 2, 2).Value = u.SamAccountName ?? string.Empty;
                        ws2.Cell(r + 2, 3).Value = u.OuPath ?? string.Empty;
                        ws2.Cell(r + 2, 4).Value = u.Description ?? string.Empty;
                        ws2.Cell(r + 2, 5).Value = u.StatusText;
                        ws2.Cell(r + 2, 6).Value = u.LastLogonText;
                    }

                    ws.Columns().AdjustToContents();
                    ws2.Columns().AdjustToContents();
                    wb.SaveAs(path);
                    lblStatus.Text = $"Exportado a Excel: {Path.GetFileName(path)}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar a Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportHtml_Click(object sender, RoutedEventArgs e)
        {
            string path = PromptSavePath("Guardar Active Directory en HTML", "Archivo HTML (*.html)|*.html", "html");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
                sb.AppendLine("<title>Active Directory - vmPing GLR</title>");
                sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2937}h1{font-size:22px}h2{font-size:17px;margin-top:28px}table{border-collapse:collapse;width:100%;font-size:12px;margin-top:8px}th,td{border:1px solid #d1d5db;padding:6px 8px;text-align:left}th{background:#f3f4f6}tr:nth-child(even){background:#f9fafb}.ok{color:#047857;font-weight:bold}.no{color:#b91c1c}</style></head><body>");
                sb.AppendLine($"<h1>Active Directory</h1><p>Generado: {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Dominio: {WebUtility.HtmlEncode(Environment.UserDomainName)}</p>");

                sb.AppendLine("<h2>Computadoras</h2>");
                sb.AppendLine("<table><thead><tr><th>Equipo</th><th>DNS</th><th>SO</th><th>OU</th><th>Estado</th><th>Último Logon</th><th>Inventario</th><th>Serie</th><th>Modelo</th></tr></thead><tbody>");
                foreach (var c in _computers)
                {
                    var state = c.Status == AdAccountStatus.Enabled ? "<span class='ok'>Habilitada</span>" : "<span class='no'>Deshabilitada</span>";
                    sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(c.Name)}</td><td>{WebUtility.HtmlEncode(c.DnsHostName)}</td><td>{WebUtility.HtmlEncode(c.OperatingSystem)}</td><td>{WebUtility.HtmlEncode(c.OuPath)}</td><td>{state}</td><td>{c.LastLogonText}</td><td>{c.InventoryStateText}</td><td>{WebUtility.HtmlEncode(c.Serials)}</td><td>{WebUtility.HtmlEncode(c.Model)}</td></tr>");
                }
                sb.AppendLine("</tbody></table>");

                sb.AppendLine("<h2>Usuarios</h2>");
                sb.AppendLine("<table><thead><tr><th>Nombre</th><th>Usuario</th><th>OU</th><th>Estado</th><th>Último Logon</th></tr></thead><tbody>");
                foreach (var u in _users)
                {
                    var state = u.Status == AdAccountStatus.Enabled ? "<span class='ok'>Habilitado</span>" : "<span class='no'>Deshabilitado</span>";
                    sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(u.DisplayName)}</td><td>{WebUtility.HtmlEncode(u.SamAccountName)}</td><td>{WebUtility.HtmlEncode(u.OuPath)}</td><td>{state}</td><td>{u.LastLogonText}</td></tr>");
                }
                sb.AppendLine("</tbody></table></body></html>");

                File.WriteAllText(path, sb.ToString());
                lblStatus.Text = $"Exportado a HTML: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar a HTML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
