using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using vmPing.Classes;
using Path = System.Windows.Shapes.Path;

namespace vmPing.UI
{
    public partial class DashboardWindow : Window
    {
        private readonly ObservableCollection<Probe> _probes;
        private readonly DispatcherTimer _timer;
        private bool _isPaused;
        private DateTime _sessionStart = DateTime.Now;
        private Probe _selectedProbe;

        public DashboardWindow(ObservableCollection<Probe> probes)
        {
            InitializeComponent();
            _probes = probes ?? new ObservableCollection<Probe>();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            Closing += (s, e) => _timer.Stop();

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
            RefreshInsights();
            RefreshAnalysisCharts();
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
                    P95Rtt = Percentile(okSamples.Select(s => (double)s.RttMs).ToList(), 95),
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

                string displayName = !string.IsNullOrWhiteSpace(p.Alias)
                    ? p.Alias
                    : (!string.IsNullOrWhiteSpace(p.Hostname) ? p.Hostname : "(sin host)");

                var hostLabel = new TextBlock
                {
                    Text = displayName.Length > 15 ? displayName.Substring(0, 14) + ".." : displayName,
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
        // ── Insights ──────────────────────────────────────────────────────
        private void RefreshInsights()
        {
            // Session-wide latency samples (only successful pings).
            var allRtts = _probes
                .SelectMany(p => p.PingSamples)
                .Where(s => s.Success && s.RttMs > 0)
                .Select(s => (double)s.RttMs)
                .ToList();

            if (allRtts.Count > 0)
            {
                double p95 = Percentile(allRtts, 95);
                double p50 = Percentile(allRtts, 50);
                insightP95.Text = $"{p95:F0} ms";
                insightP95Detail.Text = $"p50 {p50:F0} ms · {allRtts.Count} muestras";
                insightP95.ToolTip = $"Latencia p95 de la sesion: {p95:F1} ms\np50: {p50:F1} ms\nMuestras validas: {allRtts.Count}";
            }
            else
            {
                insightP95.Text = "--";
                insightP95Detail.Text = "Sin muestras";
            }

            // Most unstable host: highest packet loss, then highest p95.
            var ranked = _probes
                .Where(p => p.Statistics != null && p.Statistics.Sent > 0)
                .Select(p =>
                {
                    var ok = p.PingSamples.Where(s => s.Success).Select(s => (double)s.RttMs).ToList();
                    return new
                    {
                        Probe = p,
                        Loss = 100.0 * p.Statistics.Lost / p.Statistics.Sent,
                        P95 = Percentile(ok, 95),
                        DownEvents = Probe.StatusChangeLog.Count(c => c.Hostname == p.Hostname && c.Status == ProbeStatus.Down)
                    };
                })
                .OrderByDescending(x => x.Loss)
                .ThenByDescending(x => x.P95)
                .ThenByDescending(x => x.DownEvents)
                .ToList();

            var worst = ranked.FirstOrDefault();
            if (worst != null && (worst.Loss > 0 || worst.P95 > 0))
            {
                insightWorstHost.Text = HostDisplayName(worst.Probe);
                insightWorstHost.ToolTip = $"{worst.Probe.Hostname}\nPerdida: {worst.Loss:F1}%\np95: {worst.P95:F0} ms\nCaidas: {worst.DownEvents}";
                insightWorstDetail.Text = worst.Loss > 0
                    ? $"{worst.Loss:F1}% perdida · {worst.DownEvents} caida(s)"
                    : $"p95 {worst.P95:F0} ms · sin perdidas";
            }
            else
            {
                insightWorstHost.Text = "--";
                insightWorstDetail.Text = "Sin datos";
            }

            // Critical hour in the last 24 h (most down/error events).
            var since = DateTime.Now.AddHours(-24);
            var badEvents = Probe.StatusChangeLog
                .Where(c => c.Timestamp >= since &&
                            (c.Status == ProbeStatus.Down || c.Status == ProbeStatus.Error))
                .ToList();

            if (badEvents.Count > 0)
            {
                var peak = badEvents
                    .GroupBy(c => c.Timestamp.Hour)
                    .Select(g => new { Hour = g.Key, Count = g.Count() })
                    .OrderByDescending(g => g.Count)
                    .ThenBy(g => g.Hour)
                    .First();

                insightCriticalHour.Text = $"{peak.Hour:00}:00";
                insightCriticalDetail.Text = $"{peak.Count} evento(s) entre {peak.Hour:00}:00 y {peak.Hour:00}:59";
                insightCriticalHour.ToolTip = $"{badEvents.Count} eventos Down/Error en las ultimas 24 h.";
            }
            else
            {
                insightCriticalHour.Text = "--";
                insightCriticalDetail.Text = "Sin caidas en 24 h";
                insightCriticalHour.ToolTip = null;
            }

            // Latency trend: last 15 min vs previous 15 min.
            DateTime now = DateTime.Now;
            DateTime windowStart = now.AddMinutes(-15);
            DateTime previousStart = now.AddMinutes(-30);

            var current = _probes
                .SelectMany(p => p.PingSamples)
                .Where(s => s.Success && s.RttMs > 0 && s.Timestamp >= windowStart)
                .Select(s => (double)s.RttMs)
                .ToList();

            var previous = _probes
                .SelectMany(p => p.PingSamples)
                .Where(s => s.Success && s.RttMs > 0 && s.Timestamp >= previousStart && s.Timestamp < windowStart)
                .Select(s => (double)s.RttMs)
                .ToList();

            if (current.Count >= 3 && previous.Count >= 3)
            {
                double curAvg = current.Average();
                double prevAvg = previous.Average();
                double deltaPct = prevAvg > 0 ? 100.0 * (curAvg - prevAvg) / prevAvg : 0;

                if (Math.Abs(deltaPct) < 3)
                {
                    insightTrend.Text = "→ estable";
                    insightTrend.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                }
                else if (deltaPct > 0)
                {
                    insightTrend.Text = $"↑ {deltaPct:F0}%";
                    insightTrend.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
                }
                else
                {
                    insightTrend.Text = $"↓ {Math.Abs(deltaPct):F0}%";
                    insightTrend.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                }

                insightTrendDetail.Text = $"prev {prevAvg:F0} ms → ahora {curAvg:F0} ms";
                insightTrend.ToolTip = $"Promedio ultimos 15 min: {curAvg:F1} ms ({current.Count} muestras)\nPromedio 15 min previos: {prevAvg:F1} ms ({previous.Count} muestras)";
            }
            else
            {
                insightTrend.Text = "--";
                insightTrend.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
                insightTrendDetail.Text = "Sin datos suficientes";
                insightTrend.ToolTip = "Se necesitan al menos 3 muestras en cada ventana de 15 minutos.";
            }
        }

        // Sorts a copy of the values and returns the requested percentile (0-100).
        private static double Percentile(List<double> values, double percentile)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            var sorted = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (sorted.Count == 0)
            {
                return 0;
            }

            if (sorted.Count == 1)
            {
                return sorted[0];
            }

            double position = (percentile / 100.0) * (sorted.Count - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);

            if (lower == upper)
            {
                return sorted[lower];
            }

            double fraction = position - lower;
            return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
        }

        private static string HostDisplayName(Probe probe)
        {
            if (probe == null)
            {
                return "--";
            }

            if (!string.IsNullOrWhiteSpace(probe.Alias))
            {
                return probe.Alias;
            }

            return string.IsNullOrWhiteSpace(probe.Hostname) ? "(sin host)" : probe.Hostname;
        }

        // ── Analysis charts (Analisis tab) ────────────────────────────────
        private void DashboardTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Tab content is not measured until it is selected, so charts must
            // be redrawn when the Analisis tab becomes visible.
            if (e.Source == DashboardTabs && DashboardTabs.SelectedIndex == 1)
            {
                Dispatcher.BeginInvoke(new Action(RefreshAnalysisCharts), DispatcherPriority.Loaded);
            }
        }

        private void RefreshAnalysisCharts()
        {
            if (topLossCanvas == null || latencyCanvas == null || donutCanvas == null)
            {
                return;
            }

            if (DashboardTabs != null && DashboardTabs.SelectedIndex != 1)
            {
                return;
            }

            DrawTopLossChart();
            DrawLatencyHistogram();
            DrawAvailabilityDonut();
        }

        private void DrawTopLossChart()
        {
            var canvas = topLossCanvas;
            canvas.Children.Clear();

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 40 || h < 30)
            {
                return;
            }

            var data = _probes
                .Where(p => p.Statistics != null && p.Statistics.Sent > 0)
                .Select(p => new
                {
                    Name = HostDisplayName(p),
                    Loss = 100.0 * p.Statistics.Lost / p.Statistics.Sent,
                    Sent = p.Statistics.Sent,
                    Lost = p.Statistics.Lost
                })
                .Where(x => x.Loss > 0)
                .OrderByDescending(x => x.Loss)
                .ThenByDescending(x => x.Lost)
                .Take(6)
                .ToList();

            if (data.Count == 0)
            {
                AddChartMessage(canvas, w, h, "Sin perdidas de paquetes registradas");
                return;
            }

            double labelW = Math.Min(96, w * 0.34);
            double valueW = 44;
            double barAreaW = Math.Max(20, w - labelW - valueW);
            double rowH = h / data.Count;
            double barH = Math.Min(16, Math.Max(8, rowH * 0.52));
            double maxLoss = Math.Max(data.Max(x => x.Loss), 1);

            for (int i = 0; i < data.Count; i++)
            {
                var item = data[i];
                double centerY = (rowH * i) + (rowH / 2);

                var label = new TextBlock
                {
                    Text = Truncate(item.Name, 15),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = $"{item.Name}\n{item.Lost} de {item.Sent} paquetes perdidos"
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, centerY - 8);
                label.Width = labelW - 6;
                canvas.Children.Add(label);

                var track = new Rectangle
                {
                    Height = barH,
                    Width = barAreaW,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0))
                };
                Canvas.SetLeft(track, labelW);
                Canvas.SetTop(track, centerY - (barH / 2));
                canvas.Children.Add(track);

                double barW = Math.Max(3, barAreaW * (item.Loss / maxLoss));
                var bar = new Rectangle
                {
                    Height = barH,
                    Width = barW,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = LossBrush(item.Loss),
                    ToolTip = $"{item.Name}\nPerdida: {item.Loss:F1}%\n{item.Lost} de {item.Sent} paquetes"
                };
                Canvas.SetLeft(bar, labelW);
                Canvas.SetTop(bar, centerY - (barH / 2));
                canvas.Children.Add(bar);

                var value = new TextBlock
                {
                    Text = $"{item.Loss:F1}%",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LossBrush(item.Loss)
                };
                Canvas.SetLeft(value, labelW + barAreaW + 6);
                Canvas.SetTop(value, centerY - 8);
                canvas.Children.Add(value);
            }
        }

        private void DrawLatencyHistogram()
        {
            var canvas = latencyCanvas;
            canvas.Children.Clear();

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 40 || h < 30)
            {
                return;
            }

            var rtts = _probes
                .SelectMany(p => p.PingSamples)
                .Where(s => s.Success && s.RttMs > 0)
                .Select(s => (double)s.RttMs)
                .ToList();

            if (rtts.Count == 0)
            {
                AddChartMessage(canvas, w, h, "Sin muestras de latencia");
                if (lblHistogramCaption != null)
                {
                    lblHistogramCaption.Text = "Muestras agrupadas por rango de RTT.";
                }
                return;
            }

            // Bucket edges in milliseconds.
            double[] edges = { 0, 10, 25, 50, 100, 200, double.MaxValue };
            string[] names = { "<10", "10-25", "25-50", "50-100", "100-200", "200+" };
            var counts = new int[names.Length];

            foreach (var rtt in rtts)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (rtt >= edges[i] && rtt < edges[i + 1])
                    {
                        counts[i]++;
                        break;
                    }
                }
            }

            double padBottom = 16;
            double padTop = 14;
            double chartH = Math.Max(10, h - padBottom - padTop);
            double slotW = w / names.Length;
            int maxCount = Math.Max(counts.Max(), 1);
            double barW = Math.Max(6, slotW * 0.56);

            for (int i = 0; i < names.Length; i++)
            {
                double slotX = slotW * i;
                double barH = chartH * ((double)counts[i] / maxCount);
                double barX = slotX + ((slotW - barW) / 2);
                double barY = padTop + chartH - barH;

                if (counts[i] > 0)
                {
                    var bar = new Rectangle
                    {
                        Width = barW,
                        Height = Math.Max(2, barH),
                        RadiusX = 3,
                        RadiusY = 3,
                        Fill = new LinearGradientBrush(
                            Color.FromRgb(0x60, 0xA5, 0xFA),
                            Color.FromRgb(0x25, 0x63, 0xEB),
                            90),
                        ToolTip = $"{names[i]} ms: {counts[i]} muestras"
                    };
                    Canvas.SetLeft(bar, barX);
                    Canvas.SetTop(bar, barY);
                    canvas.Children.Add(bar);

                    var count = new TextBlock
                    {
                        Text = counts[i].ToString(),
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))
                    };
                    Canvas.SetLeft(count, barX + (barW / 2) - 6);
                    Canvas.SetTop(count, Math.Max(0, barY - 13));
                    canvas.Children.Add(count);
                }

                var label = new TextBlock
                {
                    Text = names[i],
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
                };
                Canvas.SetLeft(label, slotX + 2);
                Canvas.SetTop(label, h - padBottom + 2);
                canvas.Children.Add(label);
            }

            if (lblHistogramCaption != null)
            {
                double p50 = Percentile(rtts, 50);
                double p95 = Percentile(rtts, 95);
                lblHistogramCaption.Text = $"{rtts.Count} muestras · p50 {p50:F0} ms · p95 {p95:F0} ms";
            }
        }

        private void DrawAvailabilityDonut()
        {
            var canvas = donutCanvas;
            canvas.Children.Clear();

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 40 || h < 40)
            {
                return;
            }

            double sent = _probes.Sum(p => p.Statistics?.Sent ?? 0);
            double lost = _probes.Sum(p => p.Statistics?.Lost ?? 0);
            double received = Math.Max(0, sent - lost);

            if (sent <= 0)
            {
                AddChartMessage(canvas, w, h, "Sin trafico");
                if (lblDonutCaption != null)
                {
                    lblDonutCaption.Text = "Recibidos vs. perdidos.";
                }
                return;
            }

            double cx = w / 2;
            double cy = h / 2 - 2;
            double radius = Math.Max(16, (Math.Min(w, h) / 2) - 14);
            double thickness = Math.Max(8, radius * 0.30);

            double okPct = 100.0 * received / sent;
            double lostPct = 100.0 - okPct;

            if (lost <= 0)
            {
                var ring = new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
                    StrokeThickness = thickness,
                    ToolTip = $"Recibidos: {received:F0} de {sent:F0}"
                };
                Canvas.SetLeft(ring, cx - radius);
                Canvas.SetTop(ring, cy - radius);
                canvas.Children.Add(ring);
            }
            else
            {
                double sweepOk = 360.0 * (received / sent);
                canvas.Children.Add(CreateArc(cx, cy, radius, thickness, -90,
                    Math.Max(0.01, sweepOk), new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))));
                canvas.Children.Add(CreateArc(cx, cy, radius, thickness, -90 + sweepOk,
                    Math.Max(0.01, 360.0 - sweepOk), new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))));
            }

            var centerLabel = new TextBlock
            {
                Text = $"{okPct:F1}%",
                FontSize = Math.Max(12, radius * 0.34),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                Width = radius * 1.3,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(centerLabel, cx - (radius * 0.65));
            Canvas.SetTop(centerLabel, cy - (radius * 0.46));
            canvas.Children.Add(centerLabel);

            var subLabel = new TextBlock
            {
                Text = $"{received:F0} / {sent:F0}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Width = radius * 1.3,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(subLabel, cx - (radius * 0.65));
            Canvas.SetTop(subLabel, cy + (radius * 0.12));
            canvas.Children.Add(subLabel);

            if (lblDonutCaption != null)
            {
                lblDonutCaption.Text = $"{lost:F0} perdidos · {lostPct:F1}% de perdida";
            }

            canvas.ToolTip = $"Recibidos: {received:F0}\nPerdidos: {lost:F0}\nEnviados: {sent:F0}";
        }

        private static Path CreateArc(double cx, double cy, double radius, double thickness,
            double startAngle, double sweepAngle, Brush brush)
        {
            Point start = PointOnCircle(cx, cy, radius, startAngle);
            Point end = PointOnCircle(cx, cy, radius, startAngle + sweepAngle);

            var figure = new PathFigure
            {
                StartPoint = start,
                IsClosed = false,
                IsFilled = false
            };
            figure.Segments.Add(new ArcSegment(
                end,
                new Size(radius, radius),
                0,
                sweepAngle > 180,
                SweepDirection.Clockwise,
                true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new Path
            {
                Data = geometry,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Flat,
                StrokeEndLineCap = PenLineCap.Flat
            };
        }

        private static Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
        {
            double radians = Math.PI * angleDegrees / 180.0;
            return new Point(cx + (radius * Math.Cos(radians)), cy + (radius * Math.Sin(radians)));
        }

        private static void AddChartMessage(Canvas canvas, double w, double h, string message)
        {
            var text = new TextBlock
            {
                Text = message,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                Width = Math.Max(60, w - 8),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Canvas.SetLeft(text, 4);
            Canvas.SetTop(text, Math.Max(0, (h / 2) - 10));
            canvas.Children.Add(text);
        }

        private static Brush LossBrush(double lossPct)
        {
            if (lossPct >= 10)
            {
                return new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            }

            if (lossPct >= 2)
            {
                return new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C));
            }

            return new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, Math.Max(0, maxLength - 2)) + "..";
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            btnPause.Content = _isPaused ? "Reanudar" : "Pausar";
        }

        // ── CSV export (BI friendly) ──────────────────────────────────────
        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar datos del Dashboard a CSV",
                Filter = "CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*",
                FileName = $"vmPing_Dashboard_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                FilterIndex = 1,
                AddExtension = true,
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("hostname,alias,estado,enviados,recibidos,perdidos,perdida_pct,rtt_actual_ms,rtt_min_ms,rtt_avg_ms,rtt_max_ms,rtt_p95_ms,caidas,muestras");

                foreach (var p in _probes)
                {
                    var ok = p.PingSamples.Where(s => s.Success).Select(s => (double)s.RttMs).ToList();
                    double sent = p.Statistics?.Sent ?? 0;
                    double lost = p.Statistics?.Lost ?? 0;
                    double received = Math.Max(0, sent - lost);
                    double lossPct = sent > 0 ? (100.0 * lost / sent) : 0;
                    double minRtt = ok.Count > 0 ? ok.Min() : 0;
                    double avgRtt = ok.Count > 0 ? ok.Average() : 0;
                    double maxRtt = ok.Count > 0 ? ok.Max() : 0;
                    double p95 = Percentile(ok, 95);
                    int currentRtt = p.LatencyHistory.Count > 0 ? p.LatencyHistory.Last() : 0;
                    int downEvents = Probe.StatusChangeLog.Count(c => c.Hostname == p.Hostname && c.Status == ProbeStatus.Down);

                    sb.AppendLine(string.Join(",",
                        CsvField(p.Hostname),
                        CsvField(p.Alias),
                        CsvField(StatusToShort(p.Status)),
                        Number(sent),
                        Number(received),
                        Number(lost),
                        Number(lossPct),
                        Number(currentRtt),
                        Number(minRtt),
                        Number(avgRtt),
                        Number(maxRtt),
                        Number(p95),
                        Number(downEvents),
                        Number(p.PingSamples.Count)));
                }

                File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));

                Util.ShowInfo($"Datos exportados correctamente:\n{dialog.FileName}\n\n{_probes.Count} host(s) incluidos.");
            }
            catch (Exception ex)
            {
                Util.ShowError($"No se pudo exportar el CSV: {ex.Message}");
            }
        }

        private static string Number(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string CsvField(string value)
        {
            value = value ?? string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r', ';' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
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
            public double P95Rtt { get; set; }
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
