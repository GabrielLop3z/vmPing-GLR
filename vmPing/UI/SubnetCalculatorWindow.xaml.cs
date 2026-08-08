using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class SubnetCalculatorWindow : Window
    {
        public SubnetCalculatorWindow(string defaultIp = "")
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            // Populate CIDR dropdown (/1 to /32)
            for (int i = 32; i >= 1; i--)
            {
                uint mask = i == 0 ? 0 : 0xffffffff << (32 - i);
                string maskStr = $"{((mask >> 24) & 0xff)}.{((mask >> 16) & 0xff)}.{((mask >> 8) & 0xff)}.{mask & 0xff}";
                var item = new ComboBoxItem { Content = $"/{i} ({maskStr})", Tag = i };
                cmbCidr.Items.Add(item);
                if (i == 24) item.IsSelected = true;
            }

            if (!string.IsNullOrEmpty(defaultIp))
                txtIp.Text = defaultIp;

            Calculate();
        }

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
            Calculate();
        }

        private void Calculate()
        {
            string ipStr = txtIp.Text?.Trim();
            if (!IPAddress.TryParse(ipStr, out IPAddress ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                lblStatus.Text = "Por favor ingrese una dirección IPv4 válida.";
                return;
            }

            int cidr = 24;
            if (cmbCidr.SelectedItem is ComboBoxItem selected)
            {
                cidr = (int)selected.Tag;
            }

            uint ipBytes = IpToUint(ip);
            uint maskBytes = cidr == 0 ? 0 : 0xffffffff << (32 - cidr);
            uint netBytes = ipBytes & maskBytes;
            uint bcastBytes = netBytes | ~maskBytes;

            uint firstHost = netBytes + 1;
            uint lastHost = bcastBytes - 1;
            if (cidr >= 31)
            {
                firstHost = netBytes;
                lastHost = bcastBytes;
            }

            long totalHosts = cidr == 32 ? 1 : (1L << (32 - cidr));
            long usableHosts = cidr >= 31 ? totalHosts : Math.Max(0, totalHosts - 2);

            gridResults.Children.Clear();
            gridResults.RowDefinitions.Clear();

            int row = 0;
            void AddRow(string label, string value)
            {
                gridResults.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var lbl = new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Foreground = (Brush)FindResource("Theme.Text.Secondary"),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                Grid.SetRow(lbl, row);
                Grid.SetColumn(lbl, 0);

                var val = new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("Theme.Text.Primary"),
                    Margin = new Thickness(0, 4, 0, 4)
                };
                Grid.SetRow(val, row);
                Grid.SetColumn(val, 1);

                gridResults.Children.Add(lbl);
                gridResults.Children.Add(val);
                row++;
            }

            AddRow("Dirección de Red:", UintToIp(netBytes).ToString());
            AddRow("Máscara de Subred:", UintToIp(maskBytes).ToString());
            AddRow("Notación CIDR:", $"/{cidr}");
            AddRow("Dirección Broadcast:", UintToIp(bcastBytes).ToString());
            AddRow("Primera IP Utilizable:", UintToIp(firstHost).ToString());
            AddRow("Última IP Utilizable:", UintToIp(lastHost).ToString());
            AddRow("Total de Hosts:", $"{totalHosts:N0}");
            AddRow("Hosts Utilizables:", $"{usableHosts:N0}");
            AddRow("IP en Binario:", ToBinaryString(ipBytes));

            lblStatus.Text = $"Cálculo completado para {ipStr}/{cidr}.";
        }

        private uint IpToUint(IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private IPAddress UintToIp(uint val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            Array.Reverse(bytes);
            return new IPAddress(bytes);
        }

        private string ToBinaryString(uint val)
        {
            string b = Convert.ToString(val, 2).PadLeft(32, '0');
            return $"{b.Substring(0, 8)}.{b.Substring(8, 8)}.{b.Substring(16, 8)}.{b.Substring(24, 8)}";
        }
    }
}
