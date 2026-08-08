using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using vmPing.Classes;

namespace vmPing.UI
{
    public class MtrHopRow
    {
        public int Hop { get; set; }
        public string HostName { get; set; }
        public string IpAddress { get; set; }
        public int Sent { get; set; }
        public int Received { get; set; }
        public double LossPercent => Sent > 0 ? (double)(Sent - Received) / Sent * 100.0 : 0;
        public long LastMs { get; set; }
        public double AvgMs { get; set; }
        public long BestMs { get; set; } = long.MaxValue;
        public long WorstMs { get; set; }
        public long TotalMs { get; set; }

        public void AddSample(long ms)
        {
            Sent++;
            if (ms >= 0)
            {
                Received++;
                LastMs = ms;
                if (ms < BestMs) BestMs = ms;
                if (ms > WorstMs) WorstMs = ms;
                TotalMs += ms;
                AvgMs = (double)TotalMs / Received;
            }
            else
            {
                if (BestMs == long.MaxValue) BestMs = 0;
            }
        }
    }

    public partial class MtrWindow : Window
    {
        private CancellationTokenSource _cts;
        private readonly ObservableCollection<MtrHopRow> _hops = new ObservableCollection<MtrHopRow>();
        private bool _isRunning = false;

        public MtrWindow(string defaultHost = "")
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            if (!string.IsNullOrEmpty(defaultHost))
                txtHost.Text = defaultHost;

            dgMtr.ItemsSource = _hops;
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                StopMtr();
                return;
            }

            string host = txtHost.Text?.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Por favor ingrese un host o IP válido.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _hops.Clear();
            _isRunning = true;
            btnStart.Content = "Detener";
            lblStatus.Text = $"Descubriendo ruta hacia {host}...";

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                // Step 1: Discover route (traceroute up to 30 hops)
                var discoveredHops = await DiscoverRouteAsync(host, token);
                if (discoveredHops.Count == 0)
                {
                    lblStatus.Text = "No se pudo determinar la ruta al destino.";
                    StopMtr();
                    return;
                }

                foreach (var hop in discoveredHops)
                {
                    _hops.Add(hop);
                }

                lblStatus.Text = $"MTR en ejecución para {host} ({_hops.Count} saltos)...";

                // Step 2: Continuous ping loop across discovered hops
                await RunMtrLoopAsync(host, token);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "MTR detenido.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                StopMtr();
            }
        }

        private void StopMtr()
        {
            _cts?.Cancel();
            _isRunning = false;
            btnStart.Content = "Iniciar";
            if (lblStatus.Text.StartsWith("MTR en ejecución"))
                lblStatus.Text = "MTR detenido.";
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var h in _hops)
            {
                h.Sent = 0;
                h.Received = 0;
                h.LastMs = 0;
                h.AvgMs = 0;
                h.BestMs = long.MaxValue;
                h.WorstMs = 0;
                h.TotalMs = 0;
            }
            dgMtr.Items.Refresh();
        }

        private async Task<List<MtrHopRow>> DiscoverRouteAsync(string targetHost, CancellationToken token)
        {
            var list = new List<MtrHopRow>();
            IPAddress targetIp;
            try
            {
                var entry = await Dns.GetHostAddressesAsync(targetHost);
                targetIp = entry.FirstOrDefault();
                if (targetIp == null) return list;
            }
            catch
            {
                return list;
            }

            using (var ping = new Ping())
            {
                byte[] buffer = new byte[32];
                int timeout = 2000;

                for (int ttl = 1; ttl <= 30; ttl++)
                {
                    if (token.IsCancellationRequested) break;

                    var options = new PingOptions(ttl, true);
                    PingReply reply = null;

                    try
                    {
                        reply = await ping.SendPingAsync(targetIp, timeout, buffer, options);
                    }
                    catch { }

                    string hopAddress = reply?.Address?.ToString() ?? "*";
                    string hopName = hopAddress;

                    if (reply != null && reply.Address != null && hopAddress != "*")
                    {
                        try
                        {
                            var hostEntry = await Dns.GetHostEntryAsync(reply.Address);
                            if (!string.IsNullOrEmpty(hostEntry.HostName))
                                hopName = $"{hostEntry.HostName} ({hopAddress})";
                        }
                        catch { }
                    }

                    list.Add(new MtrHopRow
                    {
                        Hop = ttl,
                        HostName = hopName,
                        IpAddress = hopAddress
                    });

                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        break; // Reached final destination
                    }
                }
            }

            return list;
        }

        private async Task RunMtrLoopAsync(string targetHost, CancellationToken token)
        {
            byte[] buffer = new byte[32];

            while (!token.IsCancellationRequested)
            {
                foreach (var hop in _hops)
                {
                    if (token.IsCancellationRequested) break;

                    if (hop.IpAddress == "*")
                    {
                        hop.AddSample(-1);
                        continue;
                    }

                    using (var ping = new Ping())
                    {
                        try
                        {
                            var sw = Stopwatch.StartNew();
                            var reply = await ping.SendPingAsync(hop.IpAddress, 1500, buffer);
                            sw.Stop();

                            if (reply.Status == IPStatus.Success)
                            {
                                hop.AddSample(reply.RoundtripTime > 0 ? reply.RoundtripTime : sw.ElapsedMilliseconds);
                            }
                            else
                            {
                                hop.AddSample(-1);
                            }
                        }
                        catch
                        {
                            hop.AddSample(-1);
                        }
                    }

                    dgMtr.Items.Refresh();
                }

                await Task.Delay(1000, token);
            }
        }
    }
}
