using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Threading;
using System.Windows.Threading;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class DashboardWindow : Window
    {
        private readonly ObservableCollection<Probe> _probes;
        private readonly DispatcherTimer _timer;
        private bool _isPaused;
        private DateTime _sessionStart = DateTime.Now;
        private Probe _selectedProbe;
        private int _lastEventIndex;
        private List<HealthSnapshot> _lastHealthSnapshots = new List<HealthSnapshot>();
        private CancellationTokenSource _healthCts;

        public DashboardWindow(ObservableCollection<Probe> probes)
        {
            InitializeComponent();
            _probes = probes ?? new ObservableCollection<Probe>();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            Closing += (s, e) => { _timer.Stop(); _healthCts?.Cancel(); };

            RefreshAll();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_isPaused)
                RefreshAll();
        }

        private void RefreshAll()
        {
            RefreshKpis();
            RefreshHostGrid();
            RefreshRttChart();
            RefreshHeatmap();
            RefreshTimeline();
            RefreshHealth();
            lblFooter.Text = $"Sesion: {(DateTime.Now - _sessionStart):hh\\:mm\\:ss}  |  Actualizado: {DateTime.Now:HH:mm:ss}";
        }

        // ── KPIs ──────────────────────────────────────────────────────────
        private void RefreshKpis()
        {
            int total = _probes.Count;
            int up = 0, down = 0, err = 0;
            double totalRtt = 0;
            int rttCount = 0;
            double totalSent = 0, totalLost = 0;

            foreach (var p in _probes)
            {
                switch (p.Status)
                {
                    case ProbeStatus.Up:
                    case ProbeStatus.LatencyHigh:
                    case ProbeStatus.LatencyNormal:
                        up++;
                        break;
                    case ProbeStatus.Down:
                        down++;
                        break;
                    case ProbeStatus.Error:
                        err++;
                        break;
                }

                if (p.Statistics.Sent > 0)
                {
                    totalSent += p.Statistics.Sent;
                    totalLost += p.Statistics.Lost;
                }

                var okSamples = p.PingSamples.Where(s => s.Success).ToList();
                if (okSamples.Count > 0)
                {
                    totalRtt += okSamples.Average(s => s.RttMs);
                    rttCount++;
                }
            }

            kpiTotal.Text = total.ToString();
            kpiUp.Text = up.ToString();
            kpiDown.Text = down.ToString();
            kpiError.Text = err.ToString();
            kpiAvailability.Text = totalSent > 0
                ? $"{(100.0 * (totalSent - totalLost) / totalSent):F1}%"
                : "--";
            kpiAvgRtt.Text = rttCount > 0
                ? $"{(totalRtt / rttCount):F1} ms"
                : "--";
        }

        // ── Host Grid ─────────────────────────────────────────────────────
        private void RefreshHostGrid()
        {
            var selected = dgHosts.SelectedItem as HostRow;
            var rows = new List<HostRow>();

            foreach (var p in _probes)
            {
                var okSamples = p.PingSamples.Where(s => s.Success).ToList();
                double minRtt = okSamples.Count > 0 ? okSamples.Min(s => s.RttMs) : 0;
                double maxRtt = okSamples.Count > 0 ? okSamples.Max(s => s.RttMs) : 0;
                double avgRtt = okSamples.Count > 0 ? okSamples.Average(s => s.RttMs) : 0;
                double lossPct = p.Statistics.Sent > 0
                    ? (100.0 * p.Statistics.Lost / p.Statistics.Sent)
                    : 0;

                int currentRtt = 0;
                if (p.LatencyHistory.Count > 0)
                    currentRtt = p.LatencyHistory.Last();

                int downEvents = Probe.StatusChangeLog
                    .Count(c => c.Hostname == p.Hostname && c.Status == ProbeStatus.Down);

                rows.Add(new HostRow
                {
                    Hostname = p.Hostname,
                    Alias = p.Alias ?? "",
                    StatusText = StatusToShort(p.Status),
                    CurrentRtt = p.Status == ProbeStatus.Up || p.Status == ProbeStatus.LatencyHigh ? currentRtt : 0,
                    MinRtt = minRtt,
                    AvgRtt = avgRtt,
                    MaxRtt = maxRtt,
                    LossPct = lossPct,
                    DownEvents = downEvents,
                    Probe = p
                });
            }

            dgHosts.ItemsSource = rows;

            if (selected != null)
            {
                var match = rows.FirstOrDefault(r => r.Hostname == selected.Hostname);
                if (match != null)
                    dgHosts.SelectedItem = match;
            }
        }

        private static string StatusToShort(ProbeStatus s)
        {
            switch (s)
            {
                case ProbeStatus.Up: return "Up";
                case ProbeStatus.Down: return "Down";
                case ProbeStatus.Error: return "Error";
                case ProbeStatus.LatencyHigh: return "Latencia Alta";
                case ProbeStatus.LatencyNormal: return "Normalizando";
                case ProbeStatus.Indeterminate: return "Indeterm.";
                case ProbeStatus.Inactive: return "Inactivo";
                default: return s.ToString();
            }
        }

        private void DgHosts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = dgHosts.SelectedItem as HostRow;
            _selectedProbe = row?.Probe;
            RefreshRttChart();
        }

        // ── RTT Chart (Polyline on Canvas) ────────────────────────────────
        private void RefreshRttChart()
        {
            rttCanvas.Children.Clear();

            var probe = _selectedProbe;
            if (probe == null)
            {
                probe = _probes.FirstOrDefault(p => p.Status != ProbeStatus.Inactive && p.Status != ProbeStatus.Scanner);
            }

            if (probe == null || probe.PingSamples.Count == 0)
            {
                var tb = new TextBlock
                {
                    Text = "Selecciona un host para ver su grafica RTT",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Canvas.SetLeft(tb, 60);
                Canvas.SetTop(tb, 40);
                rttCanvas.Children.Add(tb);
                lblChartHost.Text = "";
                return;
            }

            lblChartHost.Text = probe.Alias ?? probe.Hostname;

            var samples = probe.PingSamples.ToList();
            if (samples.Count == 0) return;

            double w = Math.Max(rttCanvas.ActualWidth, 400);
            double h = Math.Max(rttCanvas.ActualHeight, 120);
            double padLeft = 45, padRight = 10, padTop = 10, padBottom = 25;
            double chartW = w - padLeft - padRight;
            double chartH = h - padTop - padBottom;

            double minRtt = samples.Where(s => s.Success).Select(s => (double)s.RttMs).DefaultIfEmpty(0).Min();
            double maxRtt = samples.Where(s => s.Success).Select(s => (double)s.RttMs).DefaultIfEmpty(100).Max();
            if (maxRtt == minRtt) maxRtt = minRtt + 10;
            double range = maxRtt - minRtt;

            DateTime firstTs = samples.First().Timestamp;
            DateTime lastTs = samples.Last().Timestamp;
            double timeSpan = Math.Max((lastTs - firstTs).TotalMilliseconds, 1);

            int maxPoints = (int)chartW;
            List<PingSample> drawn;
            if (samples.Count > maxPoints)
            {
                drawn = new List<PingSample>();
                double bucketSize = (double)samples.Count / maxPoints;
                for (int i = 0; i < maxPoints; i++)
                {
                    int start = (int)(i * bucketSize);
                    int end = Math.Min((int)((i + 1) * bucketSize), samples.Count);
                    var bucket = samples.GetRange(start, end - start);
                    var okBucket = bucket.Where(s => s.Success).ToList();
                    drawn.Add(new PingSample(
                        bucket[0].Timestamp,
                        okBucket.Count > 0 ? (int)okBucket.Average(s => s.RttMs) : 0,
                        okBucket.Count > 0
                    ));
                }
            }
            else
            {
                drawn = samples;
            }

            // Y-axis labels
            for (int i = 0; i <= 4; i++)
            {
                double val = minRtt + (range * i / 4.0);
                double y = padTop + chartH - (chartH * i / 4.0);
                var label = new TextBlock
                {
                    Text = $"{val:F0}",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, y - 7);
                rttCanvas.Children.Add(label);

                var gridLine = new Line
                {
                    X1 = padLeft, Y1 = y, X2 = w - padRight, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                rttCanvas.Children.Add(gridLine);
            }

            // Threshold line
            if (ApplicationOptions.LatencyDetectionMode != ApplicationOptions.LatencyMode.Off
                && ApplicationOptions.HighLatencyMilliseconds > 0)
            {
                double threshY = padTop + chartH - (chartH * (ApplicationOptions.HighLatencyMilliseconds - minRtt) / range);
                if (threshY >= padTop && threshY <= padTop + chartH)
                {
                    var threshLine = new Line
                    {
                        X1 = padLeft, Y1 = threshY, X2 = w - padRight, Y2 = threshY,
                        Stroke = new SolidColorBrush(Color.FromArgb(180, 0xEA, 0x58, 0x0C)),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 3 }
                    };
                    rttCanvas.Children.Add(threshLine);

                    var threshLabel = new TextBlock
                    {
                        Text = $"Threshold ({ApplicationOptions.HighLatencyMilliseconds}ms)",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                    };
                    Canvas.SetRight(threshLabel, padRight + 4);
                    Canvas.SetTop(threshLabel, threshY - 14);
                    rttCanvas.Children.Add(threshLabel);
                }
            }

            // Data line (success)
            var successPoints = new PointCollection();
            var failPoints = new List<Point>();

            for (int i = 0; i < drawn.Count; i++)
            {
                double x = padLeft + (i * chartW / Math.Max(drawn.Count - 1, 1));
                double norm = (drawn[i].RttMs - minRtt) / range;
                double y = padTop + chartH - (chartH * norm);

                if (drawn[i].Success)
                    successPoints.Add(new Point(x, y));
                else
                    failPoints.Add(new Point(x, y));
            }

            if (successPoints.Count > 1)
            {
                var line = new Polyline
                {
                    Points = successPoints,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                    StrokeThickness = 1.5,
                    StrokeLineJoin = PenLineJoin.Round
                };
                rttCanvas.Children.Add(line);
            }

            // Fail dots
            foreach (var fp in failPoints)
            {
                var dot = new Ellipse
                {
                    Width = 4, Height = 4,
                    Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                };
                Canvas.SetLeft(dot, fp.X - 2);
                Canvas.SetTop(dot, fp.Y - 2);
                rttCanvas.Children.Add(dot);
            }

            // X-axis time labels
            int labelCount = 5;
            for (int i = 0; i <= labelCount; i++)
            {
                double x = padLeft + (chartW * i / labelCount);
                DateTime ts = firstTs.AddMilliseconds(timeSpan * i / labelCount);
                var xLabel = new TextBlock
                {
                    Text = ts.ToString("HH:mm:ss"),
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
                };
                Canvas.SetLeft(xLabel, x - 18);
                Canvas.SetTop(xLabel, h - 16);
                rttCanvas.Children.Add(xLabel);
            }
        }

        // ── Availability Heatmap ──────────────────────────────────────────
        private void RefreshHeatmap()
        {
            heatmapPanel.Children.Clear();

            if (_probes.Count == 0) return;

            int blockMinutes = 5;
            int totalBlocks = 12;
            DateTime now = DateTime.Now;
            DateTime windowStart = now.AddMinutes(-blockMinutes * totalBlocks);

            foreach (var p in _probes)
            {
                var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

                var hostLabel = new TextBlock
                {
                    Text = (p.Alias ?? p.Hostname).Length > 15 ? (p.Alias ?? p.Hostname).Substring(0, 14) + ".." : (p.Alias ?? p.Hostname),
                    Width = 100,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                row.Children.Add(hostLabel);

                var events = Probe.StatusChangeLog
                    .Where(c => c.Hostname == p.Hostname && c.Timestamp >= windowStart)
                    .OrderBy(c => c.Timestamp)
                    .ToList();

                for (int b = 0; b < totalBlocks; b++)
                {
                    DateTime blockStart = now.AddMinutes(-blockMinutes * (totalBlocks - b));
                    DateTime blockEnd = blockStart.AddMinutes(blockMinutes);

                    Brush cellBrush;
                    if (!p.IsActive && p.Status == ProbeStatus.Inactive)
                    {
                        cellBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                    }
                    else
                    {
                        bool wasDown = false;
                        foreach (var evt in events)
                        {
                            if (evt.Timestamp >= blockStart && evt.Timestamp < blockEnd)
                            {
                                if (evt.Status == ProbeStatus.Down || evt.Status == ProbeStatus.Error)
                                    wasDown = true;
                            }
                            else if (evt.Timestamp < blockStart)
                            {
                                if (evt.Status == ProbeStatus.Down || evt.Status == ProbeStatus.Error)
                                    wasDown = true;
                                if (evt.Status == ProbeStatus.Up)
                                    wasDown = false;
                            }
                        }

                        if (!wasDown)
                        {
                            bool hasSamples = p.PingSamples.Any(s => s.Timestamp >= blockStart && s.Timestamp < blockEnd);
                            if (hasSamples)
                                cellBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                            else
                                cellBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                        }
                        else
                        {
                            cellBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                        }
                    }

                    var cell = new Border
                    {
                        Width = 22,
                        Height = 14,
                        Background = cellBrush,
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(1, 0, 1, 0),
                        ToolTip = $"{blockStart:HH:mm}-{blockEnd:HH:mm}"
                    };
                    row.Children.Add(cell);
                }

                heatmapPanel.Children.Add(row);
            }
        }

        // ── Event Timeline ────────────────────────────────────────────────
        private void RefreshTimeline()
        {
            var log = Probe.StatusChangeLog.ToList();
            int showCount = Math.Min(log.Count, 50);
            if (showCount == 0)
            {
                lblEventCount.Text = "0 eventos";
                return;
            }

            lblEventCount.Text = $"{log.Count} eventos";

            var recent = log.Skip(Math.Max(0, log.Count - showCount)).Reverse().ToList();

            var items = new List<EventItem>();
            foreach (var entry in recent)
            {
                items.Add(new EventItem
                {
                    Time = entry.Timestamp.ToString("HH:mm:ss"),
                    Host = entry.AliasIfExistOrHostname,
                    StatusText = entry.StatusAsString,
                    Status = entry.Status
                });
            }

            eventList.ItemsSource = items;
        }

        // ── Health (optional, uses HealthService if enabled) ──────────────
        private void RefreshHealth()
        {
            if (!ApplicationOptions.HealthEnabled)
            {
                healthPanel.Children.Clear();
                var noData = new TextBlock
                {
                    Text = "Salud de red no disponible (deshabilitada en Opciones)",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    Margin = new Thickness(8, 4, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                healthPanel.Children.Add(noData);
                return;
            }

            if (_lastHealthSnapshots.Count > 0)
            {
                healthPanel.Children.Clear();
                foreach (var snap in _lastHealthSnapshots)
                {
                    healthPanel.Children.Add(BuildHealthCard(snap));
                }
            }
            else
            {
                if (healthPanel.Children.Count == 0)
                {
                    var loading = new TextBlock
                    {
                        Text = "Recopilando datos de salud...",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                        Margin = new Thickness(8, 4, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    healthPanel.Children.Add(loading);
                }
                CollectHealthAsync();
            }
        }

        private async void CollectHealthAsync()
        {
            _healthCts?.Cancel();
            _healthCts = new CancellationTokenSource();

            var hosts = _probes
                .Where(p => p.IsActive && p.Status != ProbeStatus.Inactive && p.Status != ProbeStatus.Scanner)
                .Select(p => p.Hostname)
                .Distinct()
                .ToList();

            if (hosts.Count == 0) return;

            try
            {
                _lastHealthSnapshots = await HealthService.CollectAsync(hosts, null, _healthCts.Token);
                Dispatcher.Invoke(() =>
                {
                    healthPanel.Children.Clear();
                    foreach (var snap in _lastHealthSnapshots)
                        healthPanel.Children.Add(BuildHealthCard(snap));
                });
            }
            catch { }
        }

        private Border BuildHealthCard(HealthSnapshot snap)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 140
            };

            var stack = new StackPanel();

            var hostText = new TextBlock
            {
                Text = (snap.Host.Length > 16 ? snap.Host.Substring(0, 15) + ".." : snap.Host),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(hostText);

            if (snap.CpuPercent >= 0)
                stack.Children.Add(BuildHealthBar("CPU", snap.CpuPercent, GetHealthColor(snap.CpuPercent)));
            if (snap.RamPercent >= 0)
                stack.Children.Add(BuildHealthBar("RAM", snap.RamPercent, GetHealthColor(snap.RamPercent)));
            if (snap.DiskPercent >= 0)
                stack.Children.Add(BuildHealthBar("Disco", snap.DiskPercent, GetHealthColor(snap.DiskPercent)));

            if (!snap.HasData)
            {
                var noData = new TextBlock
                {
                    Text = "Sin datos",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
                };
                stack.Children.Add(noData);
            }

            card.Child = stack;
            return card;
        }

        private StackPanel BuildHealthBar(string label, double value, Color color)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };

            var header = new DockPanel();
            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))
            };
            DockPanel.SetDock(lbl, Dock.Left);
            header.Children.Add(lbl);

            var val = new TextBlock
            {
                Text = $"{value:F0}%",
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            header.Children.Add(val);
            sp.Children.Add(header);

            var barBg = new Border
            {
                Height = 4,
                Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                CornerRadius = new CornerRadius(2)
            };

            var barFill = new Border
            {
                Height = 4,
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            barBg.Child = barFill;

            barBg.SizeChanged += (s, e) =>
            {
                barFill.Width = Math.Max(0, barBg.ActualWidth * Math.Min(value, 100) / 100.0);
            };

            sp.Children.Add(barBg);
            return sp;
        }

        private static Color GetHealthColor(double pct)
        {
            if (pct < 60) return Color.FromRgb(0x16, 0xA3, 0x4A);
            if (pct < 85) return Color.FromRgb(0xEA, 0x58, 0x0C);
            return Color.FromRgb(0xDC, 0x26, 0x26);
        }

        // ── Pause / Export ────────────────────────────────────────────────
        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            btnPause.Content = _isPaused ? "Reanudar" : "Pausar";
        }

        private void BtnExportHtml_Click(object sender, RoutedEventArgs e)
        {
            var export = new ExportWindow(_probes);
            export.Owner = this;
            export.ShowDialog();
        }

        // ── Data classes ──────────────────────────────────────────────────
        public class HostRow
        {
            public string Hostname { get; set; }
            public string Alias { get; set; }
            public string StatusText { get; set; }
            public int CurrentRtt { get; set; }
            public double MinRtt { get; set; }
            public double AvgRtt { get; set; }
            public double MaxRtt { get; set; }
            public double LossPct { get; set; }
            public int DownEvents { get; set; }
            public Probe Probe { get; set; }
        }

        public class EventItem
        {
            public string Time { get; set; }
            public string Host { get; set; }
            public string StatusText { get; set; }
            public ProbeStatus Status { get; set; }

            public Brush StatusColor
            {
                get
                {
                    switch (Status)
                    {
                        case ProbeStatus.Up:
                        case ProbeStatus.LatencyNormal:
                            return new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                        case ProbeStatus.Down:
                        case ProbeStatus.Error:
                            return new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                        case ProbeStatus.Start:
                            return new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                        case ProbeStatus.Stop:
                            return new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
                        case ProbeStatus.LatencyHigh:
                            return new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C));
                        default:
                            return new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                    }
                }
            }

            public string StatusBadgeBg
            {
                get
                {
                    switch (Status)
                    {
                        case ProbeStatus.Up:
                        case ProbeStatus.LatencyNormal: return "#10B981";
                        case ProbeStatus.Down:
                        case ProbeStatus.Error: return "#EF4444";
                        case ProbeStatus.Start: return "#3B82F6";
                        case ProbeStatus.Stop: return "#6B7280";
                        case ProbeStatus.LatencyHigh: return "#F59E0B";
                        default: return "#94A3B8";
                    }
                }
            }
        }
    }
}
