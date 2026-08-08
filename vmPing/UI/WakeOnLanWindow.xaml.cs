using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using vmPing.Classes;

namespace vmPing.UI
{
    public class WolDevice
    {
        public string Name { get; set; }
        public string MacAddress { get; set; }
    }

    public partial class WakeOnLanWindow : Window
    {
        private static readonly ObservableCollection<WolDevice> SavedDevices = new ObservableCollection<WolDevice>();

        public WakeOnLanWindow(string defaultMac = "")
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            if (!string.IsNullOrEmpty(defaultMac))
                txtMac.Text = defaultMac;

            dgDevices.ItemsSource = SavedDevices;
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            string mac = txtMac.Text?.Trim();
            SendWolPacket(mac, txtName.Text?.Trim());
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            string mac = FormatMac(txtMac.Text?.Trim());
            string name = txtName.Text?.Trim();

            if (string.IsNullOrEmpty(mac) || !IsValidMac(mac))
            {
                MessageBox.Show("Por favor ingrese una dirección MAC válida (12 caracteres hexadecimales).", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(name)) name = $"Dispositivo {mac}";

            if (!SavedDevices.Any(d => d.MacAddress.Equals(mac, StringComparison.OrdinalIgnoreCase)))
            {
                SavedDevices.Add(new WolDevice { Name = name, MacAddress = mac });
                lblStatus.Text = $"Dispositivo '{name}' guardado.";
            }
        }

        private void BtnSendDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is WolDevice device)
            {
                SendWolPacket(device.MacAddress, device.Name);
            }
        }

        private void BtnRemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is WolDevice device)
            {
                SavedDevices.Remove(device);
                lblStatus.Text = $"Dispositivo '{device.Name}' eliminado.";
            }
        }

        private void SendWolPacket(string rawMac, string deviceName)
        {
            string mac = FormatMac(rawMac);
            if (string.IsNullOrEmpty(mac) || !IsValidMac(mac))
            {
                MessageBox.Show("Por favor ingrese una dirección MAC válida (ej. 00:11:22:33:44:55).", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                byte[] macBytes = ParseMac(mac);
                byte[] magicPacket = new byte[102];

                // 6 bytes of 0xFF
                for (int i = 0; i < 6; i++) magicPacket[i] = 0xFF;

                // 16 repetitions of MAC address
                for (int i = 0; i < 16; i++)
                {
                    Buffer.BlockCopy(macBytes, 0, magicPacket, 6 + i * 6, 6);
                }

                using (var client = new UdpClient())
                {
                    client.Connect(IPAddress.Broadcast, 9);
                    client.Send(magicPacket, magicPacket.Length);
                }

                string label = string.IsNullOrEmpty(deviceName) ? mac : $"{deviceName} ({mac})";
                lblStatus.Text = $"Paquete Mágico enviado con éxito a {label}.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error al enviar WoL: {ex.Message}";
            }
        }

        private bool IsValidMac(string mac)
        {
            string clean = Regex.Replace(mac, "[^0-9A-Fa-f]", "");
            return clean.Length == 12;
        }

        private string FormatMac(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return string.Empty;
            string clean = Regex.Replace(mac, "[^0-9A-Fa-f]", "");
            if (clean.Length != 12) return mac;

            return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2).ToUpperInvariant()));
        }

        private byte[] ParseMac(string mac)
        {
            string clean = Regex.Replace(mac, "[^0-9A-Fa-f]", "");
            byte[] bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                bytes[i] = byte.Parse(clean.Substring(i * 2, 2), NumberStyles.HexNumber);
            }
            return bytes;
        }
    }
}
