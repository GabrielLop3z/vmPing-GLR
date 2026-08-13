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

            // Sistema.
            txtManufacturer.Text = string.IsNullOrEmpty(device.Manufacturer) ? "-" : device.Manufacturer;
            txtModel.Text = string.IsNullOrEmpty(device.Model) ? "-" : device.Model;
            txtSerial.Text = string.IsNullOrEmpty(device.SerialNumber) ? "-" : device.SerialNumber;
            txtUuid.Text = string.IsNullOrEmpty(device.Uuid) ? "-" : device.Uuid;
            txtOs.Text = string.IsNullOrEmpty(device.OsCaption)
                ? "-"
                : $"{device.OsCaption} (build {device.OsBuildNumber}) {device.OsArchitecture}";
            txtBoot.Text = string.IsNullOrEmpty(device.LastBootUpTime) ? "-" : device.LastBootUpTime;
            txtDomain.Text = string.IsNullOrEmpty(device.Domain) ? "-" : device.Domain;

            // Hardware.
            txtCpu.Text = string.IsNullOrEmpty(device.CpuName) ? "-" : device.CpuName;
            txtCores.Text = device.CpuCores > 0
                ? $"{device.CpuCores} físicos / {device.CpuLogicalProcessors} lógicos"
                : "-";
            txtRam.Text = device.TotalRamGB > 0
                ? $"{device.TotalRamGB:0.#} GB ({device.RamSlots} slot(s))"
                : "-";
            txtDisk.Text = device.Disks.Count == 0
                ? "-"
                : string.Join("\n", device.Disks.Select(d => $"{d.Label}  {d.SizeGB:0.#} GB total, {d.FreeGB:0.#} GB libres"));

            // Red.
            txtDnsHost.Text = string.IsNullOrEmpty(device.DnsHostname) ? "-" : device.DnsHostname;
            txtIpv4.Text = string.IsNullOrEmpty(device.Ipv4) ? "-" : device.Ipv4;
            txtIpv6.Text = string.IsNullOrEmpty(device.Ipv6) ? "-" : device.Ipv6;
            txtMac.Text = string.IsNullOrEmpty(device.MacAddresses) ? "-" : device.MacAddresses;

            // SNMP.
            txtSysName.Text = string.IsNullOrEmpty(device.SnmpSysName) ? "-" : device.SnmpSysName;
            txtSysDescr.Text = string.IsNullOrEmpty(device.SnmpSysDescr) ? "-" : device.SnmpSysDescr;
            txtLocation.Text = string.IsNullOrEmpty(device.SnmpLocation) ? "-" : device.SnmpLocation;
            txtContact.Text = string.IsNullOrEmpty(device.SnmpContact) ? "-" : device.SnmpContact;
            txtUptime.Text = string.IsNullOrEmpty(device.SnmpUptime) ? "-" : device.SnmpUptime;
        }
    }
}
