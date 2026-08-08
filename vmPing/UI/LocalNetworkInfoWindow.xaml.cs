using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class LocalNetworkInfoWindow : Window
    {
        public LocalNetworkInfoWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            LoadNetworkInfo();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadNetworkInfo();
        }

        private void LoadNetworkInfo()
        {
            pnlAdapters.Children.Clear();
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            if (interfaces.Count == 0)
            {
                lblStatus.Text = "No se encontraron interfaces de red activas.";
                return;
            }

            foreach (var ni in interfaces)
            {
                var ipProps = ni.GetIPProperties();
                var card = new Border
                {
                    Background = (Brush)FindResource("Theme.Card.Background"),
                    BorderBrush = (Brush)FindResource("Theme.Card.Border"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(15),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var sp = new StackPanel();

                // Name & Type
                sp.Children.Add(new TextBlock
                {
                    Text = ni.Name,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("Theme.Text.Primary")
                });

                sp.Children.Add(new TextBlock
                {
                    Text = $"{ni.Description} ({ni.NetworkInterfaceType}) - {ni.Speed / 1000000} Mbps",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("Theme.Text.Muted"),
                    Margin = new Thickness(0, 2, 0, 10)
                });

                // Grid of values
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                int row = 0;
                void AddRow(string label, string val)
                {
                    if (string.IsNullOrEmpty(val)) return;
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var lbl = new TextBlock
                    {
                        Text = label,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 12,
                        Foreground = (Brush)FindResource("Theme.Text.Secondary"),
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    Grid.SetRow(lbl, row);
                    Grid.SetColumn(lbl, 0);

                    var v = new TextBlock
                    {
                        Text = val,
                        FontSize = 12,
                        Foreground = (Brush)FindResource("Theme.Text.Primary"),
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    Grid.SetRow(v, row);
                    Grid.SetColumn(v, 1);

                    grid.Children.Add(lbl);
                    grid.Children.Add(v);
                    row++;
                }

                // MAC
                AddRow("Dirección MAC:", FormatMac(ni.GetPhysicalAddress().ToString()));

                // IPv4 Addresses & Subnet Masks
                var v4List = ipProps.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .ToList();

                foreach (var v4 in v4List)
                {
                    AddRow("IPv4:", v4.Address.ToString());
                    if (v4.IPv4Mask != null)
                        AddRow("Máscara de Subred:", v4.IPv4Mask.ToString());
                }

                // IPv6 Addresses
                var v6List = ipProps.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(u => u.Address.ToString())
                    .ToList();
                if (v6List.Count > 0)
                    AddRow("IPv6:", string.Join(", ", v6List));

                // Default Gateway
                var gateways = ipProps.GatewayAddresses
                    .Select(g => g.Address.ToString())
                    .ToList();
                if (gateways.Count > 0)
                    AddRow("Puerta de Enlace:", string.Join(", ", gateways));

                // DNS Servers
                var dns = ipProps.DnsAddresses
                    .Select(d => d.ToString())
                    .ToList();
                if (dns.Count > 0)
                    AddRow("Servidores DNS:", string.Join(", ", dns));

                sp.Children.Add(grid);
                card.Child = sp;
                pnlAdapters.Children.Add(card);
            }

            lblStatus.Text = $"Cargadas {interfaces.Count} interfaces activas.";
        }

        private string FormatMac(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length != 12) return mac;
            return string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== INFORMACIÓN DE RED LOCAL ===");
            sb.AppendLine($"Fecha: {DateTime.Now}");
            sb.AppendLine();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var ipProps = ni.GetIPProperties();
                sb.AppendLine($"Interfaz: {ni.Name} ({ni.Description})");
                sb.AppendLine($"  MAC: {FormatMac(ni.GetPhysicalAddress().ToString())}");
                foreach (var v4 in ipProps.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    sb.AppendLine($"  IPv4: {v4.Address} / {v4.IPv4Mask}");
                }
                foreach (var g in ipProps.GatewayAddresses)
                {
                    sb.AppendLine($"  Gateway: {g.Address}");
                }
                foreach (var d in ipProps.DnsAddresses)
                {
                    sb.AppendLine($"  DNS: {d}");
                }
                sb.AppendLine();
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Información de red copiada al portapapeles.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
