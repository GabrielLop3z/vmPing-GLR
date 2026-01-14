using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using vmPing.Classes;
using vmPing.Properties;
using AutoUpdaterDotNET;

namespace vmPing.UI
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<Probe> _ProbeCollection = new ObservableCollection<Probe>();
        private Dictionary<string, string> _Aliases = new Dictionary<string, string>();
        private System.Windows.Forms.NotifyIcon NotifyIcon;
        private System.Windows.Threading.DispatcherTimer _dashboardTimer;

        public static RoutedCommand OptionsCommand = new RoutedCommand();
        public static RoutedCommand StartStopCommand = new RoutedCommand();
        public static RoutedCommand HelpCommand = new RoutedCommand();
        public static RoutedCommand NewInstanceCommand = new RoutedCommand();
        public static RoutedCommand TracerouteCommand = new RoutedCommand();
        public static RoutedCommand FloodHostCommand = new RoutedCommand();
        public static RoutedCommand NetworkScanCommand = new RoutedCommand();
        public static RoutedCommand AddProbeCommand = new RoutedCommand();
        public static RoutedCommand MultiInputCommand = new RoutedCommand();
        public static RoutedCommand StatusHistoryCommand = new RoutedCommand();
        public static RoutedCommand FullScreenCommand = new RoutedCommand();
        public static RoutedCommand CompactModeCommand = new RoutedCommand();

        private bool _isFullScreen = false;
        private bool _isCompactMode = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAplication();
        }

        private void InitializeAplication()
        {
            InitializeCommandBindings();
            LoadFavorites();
            LoadAliases();
            Configuration.Load();
            RefreshGuiState();

            _dashboardTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _dashboardTimer.Tick += (s, e) => UpdateDashboard();
            _dashboardTimer.Start();

            // Setup Grouping
            // var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_ProbeCollection);
            // view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Category"));

            // Set items source for main GUI ItemsControl.
            ProbeItemsControl.ItemsSource = _ProbeCollection;
        }

        private void UpdateDashboard()
        {
            if (_ProbeCollection == null) return;

            Dash_TotalNodes.Text = _ProbeCollection.Count.ToString();
            Dash_UpNodes.Text = _ProbeCollection.Count(p => p.Status == ProbeStatus.Up).ToString();
            Dash_DownNodes.Text = _ProbeCollection.Count(p => p.Status == ProbeStatus.Down || p.Status == ProbeStatus.Error).ToString();

            var latencies = _ProbeCollection
                .Where(p => p.IsActive && p.LatencyHistory.Count > 0)
                .Select(p => p.LatencyHistory.Last())
                .Where(l => l >= 0)
                .ToList();

            double avg = latencies.Any() ? latencies.Average() : 0;
            Dash_AvgLatency.Text = $"{avg:F1} ms";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set initial ColumnCount slider value.
            ColumnCount.Value = ApplicationOptions.InitialColumnCount > 0
                ? ApplicationOptions.InitialColumnCount
                : 2;
            ViewColumnCount = (int)ColumnCount.Value;

            // Parse command line arguments. Get any host addresses entered on command line.
            List<string> cliHosts = CommandLine.ParseArguments();

            // Add initial probes.
            if (cliHosts.Count > 0)
            {
                // Host addresses were entered on the command line.
                // Add addresses to probe collection and begin pinging.
                AddProbe(cliHosts.Count);
                for (int i = 0; i < cliHosts.Count; ++i)
                {
                    _ProbeCollection[i].Hostname = cliHosts[i];
                    _ProbeCollection[i].Alias = _Aliases.ContainsKey(_ProbeCollection[i].Hostname.ToLower())
                        ? _Aliases[_ProbeCollection[i].Hostname.ToLower()]
                        : null;
                    _ProbeCollection[i].StartStop();
                }
            }
            else
            {
                // No addresses entered on the command line.
                // Add initial blank probes.
                AddProbe(
                    (ApplicationOptions.InitialProbeCount > 0)
                        ? ApplicationOptions.InitialProbeCount
                        : 2);

                // Determine statup mode.
                switch (ApplicationOptions.InitialStartMode)
                {
                    case ApplicationOptions.StartMode.MultiInput:
                        RefreshColumnCount();
                        MultiInputWindowExecute(null, null);
                        break;
                    case ApplicationOptions.StartMode.Favorite:
                        if (ApplicationOptions.InitialFavorite != null
                            && !string.IsNullOrWhiteSpace(ApplicationOptions.InitialFavorite))
                        {
                            LoadFavorite(ApplicationOptions.InitialFavorite);
                        }
                        break;
                }
            }

            RefreshColumnCount();
        }

        private void RefreshGuiState()
        {
            // Set popup option on menu bar.
            PopupAlways.IsChecked = false;
            PopupNever.IsChecked = false;
            PopupWhenMinimized.IsChecked = false;

            switch (ApplicationOptions.PopupOption)
            {
                case ApplicationOptions.PopupNotificationOption.Always:
                    PopupAlways.IsChecked = true;
                    break;
                case ApplicationOptions.PopupNotificationOption.Never:
                    PopupNever.IsChecked = true;
                    break;
                case ApplicationOptions.PopupNotificationOption.WhenMinimized:
                    PopupWhenMinimized.IsChecked = true;
                    break;
            }

            // Set always on top state.
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            if (Probe.StatusHistoryWindow != null && Probe.StatusHistoryWindow.IsLoaded)
            {
                Probe.StatusHistoryWindow.Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            }
            if (HelpWindow._OpenWindow != null)
            {
                HelpWindow._OpenWindow.Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            }
            foreach (Probe probe in _ProbeCollection)
            {
                if (probe.IsolatedWindow != null && probe.IsolatedWindow.IsLoaded)
                {
                    probe.IsolatedWindow.Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
                }
            }
        }

        private void RefreshColumnCount()
        {
            // Directly set the Tag of the ItemsControl to the slider value.
            // The UniformGrid inside the GroupStyle binds to this Tag to determine column count.
            if (ProbeItemsControl != null)
            {
                ProbeItemsControl.Tag = (int)ColumnCount.Value;
            }
        }

        private void InitializeCommandBindings()
        {
            CommandBindings.Add(new CommandBinding(OptionsCommand, OptionsExecute));
            CommandBindings.Add(new CommandBinding(StartStopCommand, StartStopExecute));
            CommandBindings.Add(new CommandBinding(HelpCommand, HelpExecute));
            CommandBindings.Add(new CommandBinding(NewInstanceCommand, NewInstanceExecute));
            CommandBindings.Add(new CommandBinding(TracerouteCommand, TracerouteExecute));
            CommandBindings.Add(new CommandBinding(FloodHostCommand, FloodHostExecute));
            CommandBindings.Add(new CommandBinding(NetworkScanCommand, NetworkScanExecute));
            CommandBindings.Add(new CommandBinding(AddProbeCommand, AddProbeExecute));
            CommandBindings.Add(new CommandBinding(MultiInputCommand, MultiInputWindowExecute));
            CommandBindings.Add(new CommandBinding(StatusHistoryCommand, StatusHistoryExecute));
            CommandBindings.Add(new CommandBinding(FullScreenCommand, FullScreenExecute));
            CommandBindings.Add(new CommandBinding(CompactModeCommand, CompactModeExecute));
            
            InputBindings.Add(new InputBinding(
                OptionsCommand,
                new KeyGesture(Key.F10)));
            InputBindings.Add(new InputBinding(
                StartStopCommand,
                new KeyGesture(Key.F5)));
            InputBindings.Add(new InputBinding(
                HelpCommand,
                new KeyGesture(Constants.HelpKeyBinding)));
            InputBindings.Add(new InputBinding(
                NewInstanceCommand,
                new KeyGesture(Key.N, ModifierKeys.Control)));
            InputBindings.Add(new InputBinding(
                TracerouteCommand,
                new KeyGesture(Key.T, ModifierKeys.Control)));
            InputBindings.Add(new InputBinding(
                FloodHostCommand,
                new KeyGesture(Key.F, ModifierKeys.Control)));
            InputBindings.Add(new InputBinding(
                AddProbeCommand,
                new KeyGesture(Key.A, ModifierKeys.Control)));
            InputBindings.Add(new InputBinding(
                MultiInputCommand,
                new KeyGesture(Key.F2)));
            InputBindings.Add(new InputBinding(
                StatusHistoryCommand,
                new KeyGesture(Constants.StatusHistoryKeyBinding)));
            InputBindings.Add(new InputBinding(
                FullScreenCommand,
                new KeyGesture(Key.F11)));
            InputBindings.Add(new InputBinding(
                CompactModeCommand,
                new KeyGesture(Key.F9)));

            OptionsMenu.Command = OptionsCommand;
            StartStopMenu.Command = StartStopCommand;
            HelpMenu.Command = HelpCommand;
            NewInstanceMenu.Command = NewInstanceCommand;
            TracerouteMenu.Command = TracerouteCommand;
            FloodHostMenu.Command = FloodHostCommand;
            NetworkScanMenu.Command = NetworkScanCommand;
            AddProbeMenu.Command = AddProbeCommand;
            MultiInputMenu.Command = MultiInputCommand;
            StatusHistoryMenu.Command = StatusHistoryCommand;
            FullScreenMenu.Command = FullScreenCommand;
            CompactModeMenu.Command = CompactModeCommand;
            
            // Vincular el comando al evento del menú
            UpdateMenu.Click += (s, args) => 
            {
               AutoUpdater.ReportErrors = true; // Mostrar errores si falla al buscar (solo cuando es manual)
               AutoUpdater.Start("https://raw.githubusercontent.com/GabrielLop3z/vmPing-GLR/main/update.xml");
            };
        }

        private void FullScreenExecute(object sender, ExecutedRoutedEventArgs e)
        {
            if (!_isFullScreen)
            {
                // Enter Full Screen
                _isFullScreen = true;
                _isCompactMode = false; // Reset compact mode flag if set
                TitleBarGrid.Visibility = Visibility.Collapsed;
                MenuBar.Visibility = Visibility.Collapsed;
                StatusBarControl.Visibility = Visibility.Collapsed;
                DashboardBorder.Visibility = Visibility.Collapsed; // Typically full screen hides headers
                
                if (WindowState != WindowState.Maximized)
                {
                    WindowState = WindowState.Maximized;
                }
            }
            else
            {
                // Exit Full Screen
                _isFullScreen = false;
                TitleBarGrid.Visibility = Visibility.Visible;
                MenuBar.Visibility = Visibility.Visible;
                StatusBarControl.Visibility = Visibility.Visible;
                DashboardBorder.Visibility = Visibility.Visible;
            }
        }

        private void CompactModeExecute(object sender, ExecutedRoutedEventArgs e)
        {
            if (!_isCompactMode)
            {
                // Enter Compact Mode
                _isCompactMode = true;
                _isFullScreen = false; // Reset full screen flag if set
                
                // If maximized, restore to normal for compact widget feel, unless user wants full screen compact
                 if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                }

                TitleBarGrid.Visibility = Visibility.Collapsed;
                MenuBar.Visibility = Visibility.Collapsed;
                StatusBarControl.Visibility = Visibility.Collapsed;
                DashboardBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Exit Compact Mode
                _isCompactMode = false;
                TitleBarGrid.Visibility = Visibility.Visible;
                MenuBar.Visibility = Visibility.Visible;
                StatusBarControl.Visibility = Visibility.Visible;
                DashboardBorder.Visibility = Visibility.Visible;
            }
        }

        public void AddProbe(int numberOfProbes = 1)
        {
            for (; numberOfProbes > 0; --numberOfProbes)
            {
                _ProbeCollection.Add(new Probe());
            }
        }

        public void AddHostList(List<string> hosts)
        {
            if (hosts == null || hosts.Count == 0) return;

            AddProbe(hosts.Count);
            int startIndex = _ProbeCollection.Count - hosts.Count;

            for (int i = 0; i < hosts.Count; i++)
            {
                var probe = _ProbeCollection[startIndex + i];
                probe.Hostname = hosts[i];
                probe.Alias = _Aliases.ContainsKey(probe.Hostname.ToLower())
                    ? _Aliases[probe.Hostname.ToLower()]
                    : null;
                probe.StartStop();
            }
            RefreshColumnCount();
        }

        public void ProbeStartStop_Click(object sender, EventArgs e)
        {
            ((Probe)((Button)sender).DataContext).StartStop();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void ToggleDashboard_Click(object sender, RoutedEventArgs e)
        {
            DashboardBorder.Visibility = DashboardBorder.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public int ViewColumnCount
        {
            get { return (int)GetValue(ViewColumnCountProperty); }
            set { SetValue(ViewColumnCountProperty, value); }
        }

        public static readonly DependencyProperty ViewColumnCountProperty =
            DependencyProperty.Register("ViewColumnCount", typeof(int), typeof(MainWindow), new PropertyMetadata(2));

        private void ColumnCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Update the dependency property which the UniformGrid uses.
            ViewColumnCount = (int)e.NewValue;
            RefreshColumnCount();
        }

        private void Hostname_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var probe = (sender as TextBox).DataContext as Probe;
                probe.StartStop();

                if (_ProbeCollection.IndexOf(probe) < _ProbeCollection.Count - 1)
                {
                    var cp = ProbeItemsControl.ItemContainerGenerator.ContainerFromIndex(_ProbeCollection.IndexOf(probe) + 1) as ContentPresenter;
                    var tb = (TextBox)cp.ContentTemplate.FindName("Hostname", cp);
                    tb?.Focus();
                }
            }
        }

        private void RemoveProbe_Click(object sender, RoutedEventArgs e)
        {
            if (_ProbeCollection.Count <= 1)
            {
                return;
            }

            var probe = (sender as Button).DataContext as Probe;
            if (probe.IsActive)
            {
                // Stop/cancel active probe.
                probe.StartStop();
            }
            _ProbeCollection.Remove(probe);
            RefreshColumnCount();
        }

        private void MultiInputWindowExecute(object sender, ExecutedRoutedEventArgs e)
        {
            // Get list of current addresses to send to multi-input window.
            var addresses = new List<string>();
            for (int i = 0; i < _ProbeCollection.Count; ++i)
            {
                if (!string.IsNullOrWhiteSpace(_ProbeCollection[i].Hostname))
                {
                    addresses.Add(_ProbeCollection[i].Hostname.Trim());
                }
            }

            var wnd = new MultiInputWindow(addresses)
            {
                Owner = this
            };
            if (wnd.ShowDialog() == true)
            {
                RemoveAllProbes();

                if (wnd.Addresses.Count < 1)
                {
                    AddProbe();
                }
                else
                {
                    AddProbe(numberOfProbes: wnd.Addresses.Count);
                    for (int i = 0; i < wnd.Addresses.Count; ++i)
                    {
                        _ProbeCollection[i].Hostname = wnd.Addresses[i];
                        _ProbeCollection[i].Alias = _Aliases.ContainsKey(_ProbeCollection[i].Hostname.ToLower())
                            ? _Aliases[_ProbeCollection[i].Hostname.ToLower()]
                            : null;
                        _ProbeCollection[i].StartStop();
                    }
                }

                // Trigger refresh on ColumnCount (To update binding on window grid, if needed).
                double count = ColumnCount.Value;
                ColumnCount.Value = 1;
                ColumnCount.Value = count;
            }
        }

        private void StartStopExecute(object sender, ExecutedRoutedEventArgs e)
        {
            string toggleStatus = StartStopMenuHeader.Text;

            foreach (var probe in _ProbeCollection)
            {
                if (toggleStatus == "Detener Todo" && probe.IsActive)
                {
                    probe.StartStop();
                }
                else if (toggleStatus == "Iniciar Todo" && !probe.IsActive)
                {
                    probe.StartStop();
                }
            }
        }

        private void HelpExecute(object sender, ExecutedRoutedEventArgs e)
        {
            if (HelpWindow._OpenWindow == null)
            {
                new HelpWindow().Show();
            }
            else
            {
                HelpWindow._OpenWindow.Activate();
            }
        }

        private void NewInstanceExecute(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                var p = new System.Diagnostics.Process();
                p.StartInfo.FileName =
                    System.Reflection.Assembly.GetExecutingAssembly().Location;
                p.Start();
            }

            catch (Exception ex)
            {
                var errorWindow = DialogWindow.ErrorWindow($"Error al iniciar: {ex.Message}");
                errorWindow.Owner = this;
                errorWindow.ShowDialog();
            }
        }

        private void TracerouteExecute(object sender, ExecutedRoutedEventArgs e)
        {
            new TracerouteWindow().Show();
        }

        private void FloodHostExecute(object sender, ExecutedRoutedEventArgs e)
        {
            new FloodHostWindow().Show();
        }

        private void NetworkScanExecute(object sender, ExecutedRoutedEventArgs e)
        {
            new ScanWindow().Show();
        }

        private void AddProbeExecute(object sender, ExecutedRoutedEventArgs e)
        {
            txtAddHostname.Text = string.Empty;
            txtAddAlias.Text = string.Empty;
            AddProbeModal.Visibility = Visibility.Visible;
            
            // Defers focus to ensure visibility is applied first
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                txtAddHostname.Focus();
            }));
        }

        private void AddProbe_Cancel_Click(object sender, RoutedEventArgs e)
        {
            AddProbeModal.Visibility = Visibility.Collapsed;
        }

        private void AddProbe_Save_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtAddHostname.Text))
            {
                var probe = new Probe
                {
                    Hostname = txtAddHostname.Text,
                    Alias = txtAddAlias.Text
                };
                _ProbeCollection.Add(probe);
                probe.StartStop(); 
                RefreshColumnCount();
            }
            AddProbeModal.Visibility = Visibility.Collapsed;
        }

        private void AddHostname_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_Aliases == null) return;

            string host = txtAddHostname.Text.Trim().ToLower();
            if (_Aliases.ContainsKey(host))
            {
                txtAddAlias.Text = _Aliases[host];
            }
        }

        private void OptionsExecute(object sender, ExecutedRoutedEventArgs e)
        {
            // Open the options window.
            var optionsWnd = new OptionsWindow
            {
                Owner = this
            };
            if (optionsWnd.ShowDialog() == true)
            {
                RefreshGuiState();
                RefreshProbeColors();
            }
        }

        private void RefreshProbeColors()
        {
            for (int i = 0; i < _ProbeCollection.Count; ++i)
            {
                _ProbeCollection[i].Status = _ProbeCollection[i].Status;
            }
        }

        private void RemoveAllProbes()
        {
            foreach (var probe in _ProbeCollection)
            {
                if (probe.IsActive)
                {
                    probe.StartStop();
                }
            }
            _ProbeCollection.Clear();
            Probe.ActiveCount = 0;
        }

        private void LoadFavorites()
        {
            // Clear existing favorites menu.
            for (int i = FavoritesMenu.Items.Count - 1; i > 2; --i)
            {
                FavoritesMenu.Items.RemoveAt(i);
            }

            // Load favorites.
            foreach (var fav in Favorite.GetTitles())
            {
                var menuItem = new MenuItem
                {
                    Header = fav
                };
                menuItem.Click += (s, r) =>
                {
                    LoadFavorite((s as MenuItem).Header.ToString());
                };

                FavoritesMenu.Items.Add(menuItem);
            }
        }

        private void LoadFavorite(string favoriteTitle)
        {
            RemoveAllProbes();

            var favorite = Favorite.Load(favoriteTitle);
            if (favorite.Hostnames.Count < 1)
            {
                AddProbe();
            }
            else
            {
                AddProbe(numberOfProbes: favorite.Hostnames.Count);
                for (int i = 0; i < favorite.Hostnames.Count; ++i)
                {
                    _ProbeCollection[i].Hostname = favorite.Hostnames[i];
                    _ProbeCollection[i].Alias = _Aliases.ContainsKey(_ProbeCollection[i].Hostname.ToLower())
                        ? _Aliases[_ProbeCollection[i].Hostname.ToLower()]
                        : null;
                    _ProbeCollection[i].StartStop();
                }
            }

            ColumnCount.Value = 1;  // Ensure window's grid column binding is updated, if needed.
            ColumnCount.Value = favorite.ColumnCount;
            this.Title = $"{favoriteTitle} - vmPing";
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is Probe probe)
            {
                // Assign logic if we want to toggle isolated view or not.
                // For now, simple Modal.
                ProbeDetailsModal.DataContext = probe;
                ProbeDetailsModal.Visibility = Visibility.Visible;
            }
        }

        private void CloseModal_Click(object sender, RoutedEventArgs e)
        {
            ProbeDetailsModal.Visibility = Visibility.Collapsed;
            ProbeDetailsModal.DataContext = null;
        }

        private async void TestPort_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                lblPortStatus.Text = "Checking...";
                lblPortStatus.Foreground = System.Windows.Media.Brushes.Gray;
                
                int port = 80;
                if (!int.TryParse(txtPortNumber.Text, out port))
                {
                    lblPortStatus.Text = "Invalid Port";
                    lblPortStatus.Foreground = System.Windows.Media.Brushes.Orange;
                    return;
                }

                string host = probe.Hostname;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using (var client = new System.Net.Sockets.TcpClient())
                        {
                            var result = client.BeginConnect(host, port, null, null);
                            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));

                            if (!success)
                            {
                                // Timeout
                                try { client.Close(); } catch { } // Force close
                                Dispatcher.Invoke(() => 
                                {
                                    lblPortStatus.Text = $"Port {port} TIMEOUT";
                                    lblPortStatus.Foreground = System.Windows.Media.Brushes.Red;
                                });
                            }
                            else
                            {
                                // Connected (or failed instantly)
                                try
                                {
                                    client.EndConnect(result);
                                    Dispatcher.Invoke(() => 
                                    { 
                                        lblPortStatus.Text = $"Port {port} OPEN";
                                        lblPortStatus.Foreground = System.Windows.Media.Brushes.Green;
                                    });
                                }
                                catch
                                {
                                    // Connection failed
                                    Dispatcher.Invoke(() => 
                                    {
                                        lblPortStatus.Text = $"Port {port} CLOSED";
                                        lblPortStatus.Foreground = System.Windows.Media.Brushes.Red;
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                         Dispatcher.Invoke(() => 
                         {
                            lblPortStatus.Text = "Error";
                            lblPortStatus.Foreground = System.Windows.Media.Brushes.Red;
                         });
                    }
                });
            }
        }

        private void Telnet_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                string port = txtPortNumber.Text;
                try
                {
                    System.Diagnostics.Process.Start("cmd.exe", $"/c start telnet {probe.Hostname} {port}");
                }
                catch { /* Ignore errors if telnet is not installed */ }
            }
        }

        private void RDP_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                try
                {
                   System.Diagnostics.Process.Start("mstsc", $"/v:{probe.Hostname}");
                }
                catch { }
            }
        }

        private void VNC_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                // Try launching TightVNC with specific arguments
                try
                {
                    // Option A: Try standard system PATH
                   System.Diagnostics.Process.Start("tvnviewer.exe", $"-host={probe.Hostname}");
                }
                catch 
                {
                    try
                    {
                        // Option B: Try common install path (64-bit)
                        System.Diagnostics.Process.Start(@"C:\Program Files\TightVNC\tvnviewer.exe", $"-host={probe.Hostname}");
                    }
                    catch
                    {
                         try
                        {
                            // Option C: Try common install path (32-bit)
                            System.Diagnostics.Process.Start(@"C:\Program Files (x86)\TightVNC\tvnviewer.exe", $"-host={probe.Hostname}");
                        }
                        catch
                        {
                            MessageBox.Show("No se encontró 'tvnviewer.exe'. Asegúrate de que TightVNC esté instalado y en el PATH.", "Error VNC", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private void Web_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                try
                {
                    string url = $"http://{probe.Hostname}";
                    if (txtPortNumber.Text == "443") url = $"https://{probe.Hostname}";
                    else if (txtPortNumber.Text != "80" && !string.IsNullOrWhiteSpace(txtPortNumber.Text)) url += $":{txtPortNumber.Text}";
                    
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void CopyHostname_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                Clipboard.SetText(probe.Hostname);
            }
        }

        private void Traceroute_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                new TracerouteWindow(probe.Hostname).Show();
            }
        }

        private void Explorer_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", $@"\\{probe.Hostname}\c$");
                }
                catch { }
            }
        }

        private void Shares_Click(object sender, RoutedEventArgs e)
        {
            if (ProbeDetailsModal.DataContext is Probe probe)
            {
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", $@"\\{probe.Hostname}");
                }
                catch { }
            }
        }

        private void LoadAliases()
        {
            _Aliases = Alias.GetAll();
            var aliasList = _Aliases.ToList();
            aliasList.Sort((pair1, pair2) => pair1.Value.CompareTo(pair2.Value));

            // Clear existing aliases menu.
            for (int i = AliasesMenu.Items.Count - 1; i > 1; --i)
            {
                AliasesMenu.Items.RemoveAt(i);
            }

            // Load aliases.
            foreach (var alias in aliasList)
            {
                AliasesMenu.Items.Add(BuildAliasMenuItem(alias, false));
            }

            foreach (var probe in _ProbeCollection)
            {
                probe.Alias = probe.Hostname != null && _Aliases.ContainsKey(probe.Hostname.ToLower())
                    ? _Aliases[probe.Hostname.ToLower()]
                    : string.Empty;
            }
        }

        private MenuItem BuildAliasMenuItem(KeyValuePair<string, string> alias, bool isContextMenu)
        {
            var menuItem = new MenuItem
            {
                Header = alias.Value
            };

            if (isContextMenu)
            {
                menuItem.Click += (s, r) =>
                {
                    var selectedMenuItem = s as MenuItem;
                    var selectedAlias = (Probe)selectedMenuItem.DataContext;
                    selectedAlias.Hostname = _Aliases.FirstOrDefault(x => x.Value == selectedMenuItem.Header.ToString()).Key;
                    selectedAlias.StartStop();
                };
            }
            else
            {
                menuItem.Click += (s, r) =>
                {
                    var selectedAlias = s as MenuItem;

                    var didFindEmptyHost = false;
                    for (int i = 0; i < _ProbeCollection.Count; ++i)
                    {
                        if (string.IsNullOrWhiteSpace(_ProbeCollection[i].Hostname))
                        {
                            _ProbeCollection[i].Hostname = _Aliases.FirstOrDefault(x => x.Value == selectedAlias.Header.ToString()).Key;
                            _ProbeCollection[i].StartStop();
                            didFindEmptyHost = true;
                            break;
                        }
                    }

                    if (!didFindEmptyHost)
                    {
                        AddProbe();
                        _ProbeCollection[_ProbeCollection.Count - 1].Hostname = _Aliases.FirstOrDefault(x => x.Value == selectedAlias.Header.ToString()).Key;
                        _ProbeCollection[_ProbeCollection.Count - 1].StartStop();
                    }
                };
            }

            return menuItem;
        }

        private void CreateFavorite_Click(object sender, RoutedEventArgs e)
        {
            // Display new favorite window => Pass in current addresses and column count.
            // If window title ends with " - vmPing", then user currently has a
            // favorite loaded. Pass the title of that favorite to the new window.
            const string favTitle = " - vmPing";
            var newFavoriteWindow = new NewFavoriteWindow(
                hostList: _ProbeCollection.Select(x => x.Hostname).ToList(),
                columnCount: (int)ColumnCount.Value,
                title: Title.EndsWith(favTitle) ? Title.Remove(Title.Length - favTitle.Length) : string.Empty);
            newFavoriteWindow.Owner = this;
            if (newFavoriteWindow.ShowDialog() == true)
            {
                LoadFavorites();
            }
        }

        private void ManageFavorites_Click(object sender, RoutedEventArgs e)
        {
            // Open the favorites window.
            var manageFavoritesWindow = new ManageFavoritesWindow
            {
                Owner = this
            };
            manageFavoritesWindow.ShowDialog();
            LoadFavorites();
        }

        private void ManageAliases_Click(object sender, RoutedEventArgs e)
        {
            // Open the aliases window.
            var manageAliasesWindow = new ManageAliasesWindow
            {
                Owner = this
            };
            manageAliasesWindow.ShowDialog();
            LoadAliases();
        }

        private void PopupAlways_Click(object sender, RoutedEventArgs e)
        {
            PopupAlways.IsChecked = true;
            PopupNever.IsChecked = false;
            PopupWhenMinimized.IsChecked = false;
            ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.Always;
        }

        private void PopupNever_Click(object sender, RoutedEventArgs e)
        {
            PopupAlways.IsChecked = false;
            PopupNever.IsChecked = true;
            PopupWhenMinimized.IsChecked = false;
            ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.Never;
        }

        private void PopupWhenMinimized_Click(object sender, RoutedEventArgs e)
        {
            PopupAlways.IsChecked = false;
            PopupNever.IsChecked = false;
            PopupWhenMinimized.IsChecked = true;
            ApplicationOptions.PopupOption = ApplicationOptions.PopupNotificationOption.WhenMinimized;
        }

        private void IsolatedView_Click(object sender, RoutedEventArgs e)
        {
            var probe = (sender as Button).DataContext as Probe;
            if (probe.IsolatedWindow == null || probe.IsolatedWindow.IsLoaded == false)
            {
                new IsolatedPingWindow(probe).Show();
            }
            else if (probe.IsolatedWindow.IsLoaded)
            {
                probe.IsolatedWindow.Focus();
            }
        }

        private void EditAlias_Click(object sender, RoutedEventArgs e)
        {
            var probe = (sender as Button).DataContext as Probe;

            if (string.IsNullOrEmpty(probe.Hostname))
            {
                return;
            }

            if (_Aliases.ContainsKey(probe.Hostname.ToLower()))
            {
                probe.Alias = _Aliases[probe.Hostname.ToLower()];
            }
            else
            {
                probe.Alias = string.Empty;
            }

            var wnd = new EditAliasWindow(probe)
            {
                Owner = this
            };

            if (wnd.ShowDialog() == true)
            {
                LoadAliases();
            }
            Focus();
        }

        private void StatusHistoryExecute(object sender, ExecutedRoutedEventArgs e)
        {
            if (Probe.StatusHistoryWindow == null || Probe.StatusHistoryWindow.IsLoaded == false)
            {
                var wnd = new StatusHistoryWindow(Probe.StatusChangeLog);
                Probe.StatusHistoryWindow = wnd;
                wnd.Show();
            }
            else if (Probe.StatusHistoryWindow.IsLoaded)
            {
                Probe.StatusHistoryWindow.Focus();
            }
        }

        private void Hostname_Loaded(object sender, RoutedEventArgs e)
        {
            // Set focus to textbox on newly added monitors.  If the hostname field is blank for any existing monitors, do not change focus.
            for (int i = 0; i < _ProbeCollection.Count - 1; ++i)
            {
                if (string.IsNullOrEmpty(_ProbeCollection[i].Hostname))
                {
                    return;
                }
            }
            ((TextBox)sender).Focus();
        }

        private void Hostname_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Check if there is an alias for the hostname as you type.
            var probe = (sender as TextBox).DataContext as Probe;
            if (probe.Hostname != null)
            {
                probe.Alias = _Aliases.ContainsKey(probe.Hostname.ToLower())
                    ? _Aliases[probe.Hostname.ToLower()]
                    : null;
            }
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            // Set initial focus first text box.
            if (_ProbeCollection.Count > 0)
            {
                var cp = ProbeItemsControl.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter;
                var tb = (TextBox)cp.ContentTemplate.FindName("Hostname", cp);
                tb?.Focus();
            }
        }

        private void Logo_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
        {
            // This event is tied to the background image that appears in each probe window.
            // After a probe is started, this event removes the image from the ItemsControl.
            var image = (sender as Image);
            if (image.Visibility == Visibility.Collapsed)
            {
                image.Visibility = Visibility.Collapsed;
                image.Source = null;
            }
        }

        private void ProbeTitle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var data = new DataObject();
                data.SetData("Source", (sender as Label).DataContext as Probe);
                DragDrop.DoDragDrop(sender as DependencyObject, data, DragDropEffects.Move);
                e.Handled = true;
            }
        }

        private void History_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Probe_Drop(object sender, DragEventArgs e)
        {
            var source = e.Data.GetData("Source") as Probe;
            if (source != null)
            {
                int newIndex;
                if (sender is Label)
                {
                    newIndex = _ProbeCollection.IndexOf((sender as Label).DataContext as Probe);
                    e.Handled = true;
                }
                else if (sender is DockPanel)
                {
                    newIndex = _ProbeCollection.IndexOf((sender as DockPanel).DataContext as Probe);
                    e.Handled = true;
                }
                else
                {
                    return;
                }

                int prevIndex = _ProbeCollection.IndexOf(source);
                if (newIndex != prevIndex)
                {
                    _ProbeCollection.RemoveAt(prevIndex);
                    _ProbeCollection.Insert(newIndex, source);
                }
            }
        }

        private void HideToTray()
        {
            Visibility = Visibility.Hidden;
            WindowState = WindowState.Minimized;
            try
            {
                if (NotifyIcon == null)
                {
                    // Build context menu for tray icon.
                    System.Windows.Forms.ContextMenuStrip menuStrip = new System.Windows.Forms.ContextMenuStrip();
                    System.Windows.Forms.ToolStripMenuItem menuOptions = new System.Windows.Forms.ToolStripMenuItem("Options");
                    menuOptions.Click += (s, args) => OptionsExecute(null, null);
                    System.Windows.Forms.ToolStripMenuItem menuStatusHistory = new System.Windows.Forms.ToolStripMenuItem("Status History");
                    menuStatusHistory.Click += (s, args) => StatusHistoryExecute(null, null);
                    System.Windows.Forms.ToolStripMenuItem menuExit = new System.Windows.Forms.ToolStripMenuItem("Exit vmPing");
                    menuExit.Click += (s, args) => Application.Current.Shutdown();

                    menuStrip.Items.Add(menuOptions);
                    menuStrip.Items.Add(menuStatusHistory);
                    menuStrip.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                    menuStrip.Items.Add(menuExit);

                    // Create tray icon.
                    NotifyIcon = new System.Windows.Forms.NotifyIcon
                    {
                        Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("pack://application:,,,/vmPing.ico")).Stream),
                        Text = "vmPing",
                        ContextMenuStrip = menuStrip
                    };
                    NotifyIcon.MouseUp += NotifyIcon_MouseUp;
                }
                NotifyIcon.Visible = true;
            }
            catch
            {
                Visibility = Visibility.Visible;
            }
        }

        private void RestoreFromTray()
        {
            NotifyIcon.Visible = false;
            WindowState = WindowState.Minimized;
            Visibility = Visibility.Visible;
            Show();
            WindowState = WindowState.Normal;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && ApplicationOptions.IsMinimizeToTrayEnabled)
            {
                HideToTray();
            }
        }

        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Clear notify icon - handles notify icon cleanup when vmPing is minimized to tray
            // and user clicks a popup alert window to restore the vmPing window.
            if (IsVisible && NotifyIcon != null && NotifyIcon.Visible)
            {
                RestoreFromTray();
            }
        }

        private void NotifyIcon_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                // Left click. Restore application window.
                RestoreFromTray();
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                // Right click. Display context menu.
                System.Reflection.MethodInfo mi = typeof(System.Windows.Forms.NotifyIcon)
                    .GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                mi.Invoke(NotifyIcon, null);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (ApplicationOptions.IsExitToTrayEnabled)
            {
                HideToTray();
                e.Cancel = true;
            }
            else
            {
                NotifyIcon?.Dispose();
            }
        }

        private void History_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = sender as TextBox;
            tb.SelectionStart = (tb.DataContext as Probe).SelStart;
            tb.SelectionLength = (tb.DataContext as Probe).SelLength;
            if (!tb.IsMouseCaptureWithin && tb.SelectionLength == 0)
            {
                tb.ScrollToEnd();
            }
        }

        private void History_SelectionChanged(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            (tb.DataContext as Probe).SelStart = tb.SelectionStart;
            (tb.DataContext as Probe).SelLength = tb.SelectionLength;
        }
    }
}