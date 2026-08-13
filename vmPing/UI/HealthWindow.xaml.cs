using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClosedXML.Excel;
using Microsoft.Win32;
using vmPing.Classes;

namespace vmPing.UI
{
    public class HostHealth : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly int _maxPoints;
        private readonly List<double> _cpu = new List<double>();
        private readonly List<double> _ram = new List<double>();
        private readonly List<double> _disk = new List<double>();

        public string Host { get; set; }
        public string Alias { get; set; }

        public string CpuText { get; private set; } = "--";
        public string RamText { get; private set; } = "--";
        public string DiskText { get; private set; } = "--";
        public string DisksText { get; private set; } = string.Empty;
        public string LastUpdateText { get; private set; } = string.Empty;

        public PointCollection CpuPoints { get; private set; } = new PointCollection();
        public PointCollection RamPoints { get; private set; } = new PointCollection();
        public PointCollection DiskPoints { get; private set; } = new PointCollection();

        public HostHealth(string host, string alias, int maxPoints)
        {
            Host = host;
            Alias = alias;
            _maxPoints = maxPoints;
        }

        public void AddSample(HealthSnapshot snapshot)
        {
            _cpu.Add(snapshot.CpuPercent >= 0 ? snapshot.CpuPercent : 0);
            _ram.Add(snapshot.RamPercent >= 0 ? snapshot.RamPercent : 0);
            _disk.Add(snapshot.DiskPercent >= 0 ? snapshot.DiskPercent : 0);

            while (_cpu.Count > _maxPoints) _cpu.RemoveAt(0);
            while (_ram.Count > _maxPoints) _ram.RemoveAt(0);
            while (_disk.Count > _maxPoints) _disk.RemoveAt(0);

            CpuText = FormatPercent(snapshot.CpuPercent);
            RamText = FormatPercent(snapshot.RamPercent);
            DiskText = FormatPercent(snapshot.DiskPercent);
            DisksText = string.Join("  |  ", snapshot.Disks.Select(d => $"{d.Label} {d.UsedPercent:0}%"));
            LastUpdateText = snapshot.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

            CpuPoints = BuildSparkline(_cpu);
            RamPoints = BuildSparkline(_ram);
            DiskPoints = BuildSparkline(_disk);

            Notify("CpuText");
            Notify("RamText");
            Notify("DiskText");
            Notify("DisksText");
            Notify("LastUpdateText");
            Notify("CpuPoints");
            Notify("RamPoints");
            Notify("DiskPoints");
        }

        private string FormatPercent(double value)
        {
            return value < 0 ? "--" : $"{value:0}%";
        }

        private static PointCollection BuildSparkline(List<double> values)
        {
            const double width = 148;
            const double height = 28;
            var points = new PointCollection();
            if (values == null || values.Count == 0)
            {
                return points;
            }
            if (values.Count == 1)
            {
                var y = height - Math.Min(100, Math.Max(0, values[0])) / 100.0 * height;
                points.Add(new Point(0, y));
                points.Add(new Point(width, y));
                return points;
            }
            double step = width / (values.Count - 1);
            for (int i = 0; i < values.Count; i++)
            {
                double x = i * step;
                double y = height - Math.Min(100, Math.Max(0, values[i])) / 100.0 * height;
                points.Add(new Point(x, y));
            }
            return points;
        }

        private void Notify(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    public partial class HealthWindow : Window
    {
        private readonly string[] _hosts;
        private readonly int _maxPoints;
        private List<HostHealth> _items = new List<HostHealth>();
        private readonly DispatcherTimer _timer;
        private bool _collecting;
        private bool _paused;

        public HealthWindow(List<string> hosts)
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _hosts = hosts?.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToArray() ?? new string[0];
            _maxPoints = Math.Max(10, Math.Min(240, ApplicationOptions.HealthHistoryPoints));
            txtInterval.Text = ApplicationOptions.HealthIntervalSeconds.ToString();

            if (_hosts.Length == 0)
            {
                lblStatus.Text = "No hay equipos activos para monitorear.";
                btnToggle.IsEnabled = false;
                btnNow.IsEnabled = false;
                return;
            }

            var aliases = Alias.GetAll();
            _items = _hosts.Select(h => new HostHealth(h, aliases.ContainsKey(h.ToLower()) ? aliases[h.ToLower()] : h, _maxPoints)).ToList();
            dgHealth.ItemsSource = _items;
            lblCount.Text = $"{_hosts.Length} equipo(s)";

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(ApplicationOptions.HealthIntervalSeconds);
            _timer.Tick += async (s, e) => await CollectTickAsync();
            _timer.Start();
        }

        private async System.Threading.Tasks.Task CollectTickAsync()
        {
            if (_collecting || _paused)
            {
                return;
            }
            _collecting = true;
            try
            {
                var snapshots = await HealthService.CollectAsync(_hosts, null, CancellationToken.None).ConfigureAwait(false);
                var byHost = snapshots.ToDictionary(s => s.Host, StringComparer.OrdinalIgnoreCase);
                Dispatcher.Invoke(() =>
                {
                    foreach (var item in _items)
                    {
                        if (byHost.TryGetValue(item.Host, out var snap))
                        {
                            item.AddSample(snap);
                        }
                    }
                    lblStatus.Text = $"Última recolección: {DateTime.Now:HH:mm:ss}. Intervalo: {_timer.Interval.TotalSeconds:0} s.";
                });
            }
            catch
            {
                // Keep monitoring loop alive.
            }
            finally
            {
                _collecting = false;
            }
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            _paused = !_paused;
            btnToggle.Content = _paused ? "Reanudar" : "Pausar";
            lblStatus.Text = _paused ? "Monitoreo en pausa." : "Monitoreo reanudado.";
            if (!_paused)
            {
                _ = CollectTickAsync();
            }
        }

        private void BtnNow_Click(object sender, RoutedEventArgs e)
        {
            _ = CollectTickAsync();
        }

        private void BtnApplyInterval_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtInterval.Text, out int seconds) || seconds < 1 || seconds > 120)
            {
                Util.ShowInfo("Ingrese un intervalo válido (1-120 segundos).");
                return;
            }
            ApplicationOptions.HealthIntervalSeconds = seconds;
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            lblStatus.Text = $"Intervalo actualizado a {seconds} s.";
        }

        private void NumericTextbox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer?.Stop();
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Guardar salud en vivo (Excel)",
                Filter = "Archivo Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                FileName = $"Salud_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Salud");

                    string[] headers = { "Equipo", "Alias", "CPU (%)", "Memoria (%)", "Disco (%)", "Discos (detalle)", "Hora" };
                    for (int c = 0; c < headers.Length; c++)
                        ws.Cell(1, c + 1).Value = headers[c];

                    for (int r = 0; r < _items.Count; r++)
                    {
                        var it = _items[r];
                        ws.Cell(r + 2, 1).Value = it.Host;
                        ws.Cell(r + 2, 2).Value = it.Alias ?? string.Empty;
                        ws.Cell(r + 2, 3).Value = it.CpuText;
                        ws.Cell(r + 2, 4).Value = it.RamText;
                        ws.Cell(r + 2, 5).Value = it.DiskText;
                        ws.Cell(r + 2, 6).Value = it.DisksText ?? string.Empty;
                        ws.Cell(r + 2, 7).Value = it.LastUpdateText;
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
                Title = "Guardar salud en vivo (HTML)",
                Filter = "Archivo HTML (*.html)|*.html",
                DefaultExt = "html",
                FileName = $"Salud_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
                sb.AppendLine("<title>Salud en Vivo - vmPing GLR</title>");
                sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2937}h1{font-size:22px}table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #d1d5db;padding:6px 8px;text-align:left}th{background:#f3f4f6}tr:nth-child(even){background:#f9fafb}</style></head><body>");
                sb.AppendLine($"<h1>Salud en Vivo</h1><p>Generado: {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Equipos: {_items.Count}</p>");
                sb.AppendLine("<table><thead><tr><th>Equipo</th><th>Alias</th><th>CPU (%)</th><th>Memoria (%)</th><th>Disco (%)</th><th>Discos</th><th>Hora</th></tr></thead><tbody>");

                foreach (var it in _items)
                {
                    sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(it.Host)}</td><td>{System.Net.WebUtility.HtmlEncode(it.Alias)}</td><td>{it.CpuText}</td><td>{it.RamText}</td><td>{it.DiskText}</td><td>{System.Net.WebUtility.HtmlEncode(it.DisksText)}</td><td>{it.LastUpdateText}</td></tr>");
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