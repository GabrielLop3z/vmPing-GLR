using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using vmPing.Classes;

namespace vmPing.UI
{
    public class PortScanResult
    {
        public int Port { get; set; }
        public string Service { get; set; }
        public string Status { get; set; }
        public long ResponseTimeMs { get; set; }
    }

    public partial class PortScannerWindow : Window
    {
        private CancellationTokenSource _cts;
        private readonly ObservableCollection<PortScanResult> _results = new ObservableCollection<PortScanResult>();

        private static readonly Dictionary<int, string> KnownPorts = new Dictionary<int, string>
        {
            { 21, "FTP" }, { 22, "SSH" }, { 23, "Telnet" }, { 25, "SMTP" }, { 53, "DNS" },
            { 80, "HTTP" }, { 110, "POP3" }, { 135, "RPC" }, { 139, "NetBIOS" }, { 143, "IMAP" },
            { 443, "HTTPS" }, { 445, "SMB" }, { 465, "SMTPS" }, { 587, "Submission" }, { 993, "IMAPS" },
            { 1433, "MSSQL" }, { 1521, "Oracle" }, { 3306, "MySQL" }, { 3389, "RDP" }, { 5432, "PostgreSQL" },
            { 5900, "VNC" }, { 8080, "HTTP Alt" }, { 8443, "HTTPS Alt" }, { 27017, "MongoDB" }
        };

        public PortScannerWindow(string defaultHost = "")
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            if (!string.IsNullOrEmpty(defaultHost))
                txtHost.Text = defaultHost;

            dgResults.ItemsSource = _results;
        }

        private void CmbPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (txtStartPort == null || txtEndPort == null) return;

            switch (cmbPresets.SelectedIndex)
            {
                case 0: // 1-1024
                    txtStartPort.Text = "1";
                    txtEndPort.Text = "1024";
                    break;
                case 1: // Web
                    txtStartPort.Text = "80";
                    txtEndPort.Text = "8443";
                    break;
                case 2: // Remote
                    txtStartPort.Text = "22";
                    txtEndPort.Text = "5900";
                    break;
                case 3: // DB
                    txtStartPort.Text = "1433";
                    txtEndPort.Text = "27017";
                    break;
                case 4: // Mail
                    txtStartPort.Text = "25";
                    txtEndPort.Text = "993";
                    break;
            }
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                btnScan.IsEnabled = false;
                lblStatus.Text = "Cancelando...";
                return;
            }

            string host = txtHost.Text?.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Por favor ingrese un host o IP válido.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<int> portsToScan = GetPortsToScan();
            if (portsToScan.Count == 0)
            {
                MessageBox.Show("Rango de puertos no válido.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _results.Clear();
            _cts = new CancellationTokenSource();
            btnScan.Content = "Cancelar";
            progressBar.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            progressBar.Maximum = portsToScan.Count;

            lblStatus.Text = $"Escaneando {portsToScan.Count} puertos en {host}...";
            bool showOnlyOpen = chkOpenOnly.IsChecked == true;

            int scanned = 0;
            var token = _cts.Token;

            try
            {
                var tasks = portsToScan.Select(async port =>
                {
                    if (token.IsCancellationRequested) return;

                    var res = await CheckPortAsync(host, port, 1200, token);
                    Interlocked.Increment(ref scanned);

                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = scanned;
                        if (res != null && (!showOnlyOpen || res.Status == "Abierto"))
                        {
                            _results.Add(res);
                        }
                    });
                });

                await Task.WhenAll(tasks);

                lblStatus.Text = token.IsCancellationRequested
                    ? $"Escaneo cancelado. Se encontraron {_results.Count} puertos."
                    : $"Escaneo completado. Se encontraron {_results.Count} puertos.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                btnScan.Content = "Escanear";
                btnScan.IsEnabled = true;
                progressBar.Visibility = Visibility.Collapsed;
            }
        }

        private List<int> GetPortsToScan()
        {
            var list = new List<int>();
            if (cmbPresets.SelectedIndex == 1) return new List<int> { 80, 443, 8080, 8443 };
            if (cmbPresets.SelectedIndex == 2) return new List<int> { 22, 23, 3389, 5900 };
            if (cmbPresets.SelectedIndex == 3) return new List<int> { 1433, 1521, 3306, 5432, 27017 };
            if (cmbPresets.SelectedIndex == 4) return new List<int> { 25, 110, 143, 465, 587, 993 };

            if (int.TryParse(txtStartPort.Text, out int start) && int.TryParse(txtEndPort.Text, out int end))
            {
                if (start > 0 && end >= start && end <= 65535)
                {
                    for (int i = start; i <= end; i++) list.Add(i);
                }
            }
            return list;
        }

        private Task<PortScanResult> CheckPortAsync(string host, int port, int timeoutMs, CancellationToken token)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using (var client = new TcpClient())
                    {
                        var ar = client.BeginConnect(host, port, null, null);
                        bool connected = ar.AsyncWaitHandle.WaitOne(timeoutMs, false);
                        sw.Stop();

                        if (connected && client.Connected)
                        {
                            client.EndConnect(ar);
                            KnownPorts.TryGetValue(port, out string service);
                            return new PortScanResult
                            {
                                Port = port,
                                Service = service ?? "Desconocido",
                                Status = "Abierto",
                                ResponseTimeMs = sw.ElapsedMilliseconds
                            };
                        }
                    }
                }
                catch { }

                sw.Stop();
                KnownPorts.TryGetValue(port, out string s);
                return new PortScanResult
                {
                    Port = port,
                    Service = s ?? "Desconocido",
                    Status = "Cerrado",
                    ResponseTimeMs = sw.ElapsedMilliseconds
                };
            }, token);
        }
    }
}
