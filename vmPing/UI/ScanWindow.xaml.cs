using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class ScanWindow : Window
    {
        public ObservableCollection<ScanResult> Results { get; set; } = new ObservableCollection<ScanResult>();
        private CancellationTokenSource _cts;

        public ScanWindow()
        {
            InitializeComponent();
            ResultGrid.ItemsSource = Results;
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            if (BtnScan.Content.ToString() == "Stop")
            {
                _cts?.Cancel();
                BtnScan.Content = "Start Scan";
                StatusText.Text = "Scan cancelled.";
                ScanProgress.Visibility = Visibility.Collapsed;
                return;
            }

            if (!IPAddress.TryParse(StartIP.Text, out IPAddress start) || !IPAddress.TryParse(EndIP.Text, out IPAddress end))
            {
                MessageBox.Show("Invalid IP Addresses.");
                return;
            }

            Results.Clear();
            _cts = new CancellationTokenSource();
            BtnScan.Content = "Stop";
            ScanProgress.Visibility = Visibility.Visible;
            ScanProgress.IsIndeterminate = false;

            var ips = GetIPRange(start, end);
            ScanProgress.Maximum = ips.Count;
            ScanProgress.Value = 0;
            StatusText.Text = $"Scanning {ips.Count} hosts...";

            // Semaphore to limit concurrency
            using (var semaphore = new SemaphoreSlim(50))
            {
                var tasks = ips.Select(async ip =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (_cts.IsCancellationRequested) return;

                        var ping = new Ping();
                        try
                        {
                            var reply = await ping.SendPingAsync(ip, 1000); // 1s timeout
                            if (reply.Status == IPStatus.Success)
                            {
                                string hostname = "";
                                try
                                {
                                    var entry = await Dns.GetHostEntryAsync(ip);
                                    hostname = entry.HostName;
                                }
                                catch { }

                                Dispatcher.Invoke(() =>
                                {
                                    Results.Add(new ScanResult { IP = ip.ToString(), Status = "Up", Hostname = hostname });
                                });
                            }
                        }
                        catch { }
                    }
                    finally
                    {
                        semaphore.Release();
                        Dispatcher.Invoke(() => ScanProgress.Value++);
                    }
                });

                await Task.WhenAll(tasks);
            }

            BtnScan.Content = "Start Scan";
            ScanProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = "Scan complete.";
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var active = Results.Where(r => r.Status == "Up").ToList();
            if (active.Count == 0) return;

            // Add to main window
            // Assuming we can access MainWindow's probe collection or use CommandLine.
            // Or easier, create new probes via MainWindow instance.
            if (Application.Current.MainWindow is MainWindow mw)
            {
                var hosts = active.Select(r => !string.IsNullOrEmpty(r.Hostname) ? r.Hostname : r.IP).ToList();
                mw.AddHostList(hosts);
                this.Close();
            }
        }

        private List<IPAddress> GetIPRange(IPAddress start, IPAddress end)
        {
            var startBytes = start.GetAddressBytes().Reverse().ToArray();
            var endBytes = end.GetAddressBytes().Reverse().ToArray();
            var startInt = BitConverter.ToUInt32(startBytes, 0);
            var endInt = BitConverter.ToUInt32(endBytes, 0);

            var list = new List<IPAddress>();
            if (startInt > endInt) return list;

            for (uint i = startInt; i <= endInt; i++)
            {
                var bytes = BitConverter.GetBytes(i).Reverse().ToArray();
                list.Add(new IPAddress(bytes));
            }
            return list;
        }
    }

    public class ScanResult
    {
        public string IP { get; set; }
        public string Status { get; set; }
        public string Hostname { get; set; }
    }
}
