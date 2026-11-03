using System;
using System.Linq;
using System.Windows;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class DeviceInfoWindow : Window
    {
        public DeviceInfoWindow(DeviceInventory device)
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            var displayName = !string.IsNullOrEmpty(device.Alias)
                ? $"{device.Alias} ({device.Host})"
                : device.Host;
            Title = $"Detalle de Equipo - {displayName}";
            lblTitle.Text = displayName;
            lblHost.Text = $"Recolectado: {device.CollectedLocalText} | Fuente: {device.Source} | {device.IsReachableText}";

            if (!device.IsReachable && device.ErrorMessage != null)
            {
                lblHost.Text += $" | {device.ErrorMessage}";
            }

            lblSource.Text = device.Source ?? "Sin datos";

            // SNMP.
            txtSysName.Text = string.IsNullOrEmpty(device.SnmpSysName) ? "-" : device.SnmpSysName;
            txtSysDescr.Text = string.IsNullOrEmpty(device.SnmpSysDescr) ? "-" : device.SnmpSysDescr;
            txtLocation.Text = string.IsNullOrEmpty(device.SnmpLocation) ? "-" : device.SnmpLocation;
            txtContact.Text = string.IsNullOrEmpty(device.SnmpContact) ? "-" : device.SnmpContact;
            txtUptime.Text = string.IsNullOrEmpty(device.SnmpUptime) ? "-" : device.SnmpUptime;
        }
    }
}
