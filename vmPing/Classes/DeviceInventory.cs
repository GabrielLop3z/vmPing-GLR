using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace vmPing.Classes
{
    public class DeviceInventory
    {
        public string Host { get; set; }
        public string Alias { get; set; }
        public string Source { get; set; }          // "WMI", "SNMP", "WMI + SNMP" o "Sin datos"
        public bool IsReachable { get; set; }
        public DateTime? CollectedUtc { get; set; }
        public string ErrorMessage { get; set; }

        public string IsReachableText => IsReachable ? "Disponible" : "Sin datos";

        public string CollectedLocalText => CollectedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

        // Sistema.
        public string SerialNumber { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string Uuid { get; set; }
        public string OsCaption { get; set; }
        public string OsVersion { get; set; }
        public string OsBuildNumber { get; set; }
        public string OsArchitecture { get; set; }
        public string LastBootUpTime { get; set; }
        public string Domain { get; set; }
        public string DnsHostname { get; set; }
        public string SystemType { get; set; }

        // Hardware.
        public string CpuName { get; set; }
        public int CpuCores { get; set; }
        public int CpuLogicalProcessors { get; set; }
        public double TotalRamGB { get; set; }
        public int RamSlots { get; set; }

        // Almacenamiento.
        public List<DiskInfo> Disks { get; set; } = new List<DiskInfo>();

        // Red.
        public string MacAddresses { get; set; }
        public string Ipv4 { get; set; }
        public string Ipv6 { get; set; }
        public string DhcpEnabled { get; set; }

        // SNMP.
        public string SnmpSysName { get; set; }
        public string SnmpSysDescr { get; set; }
        public string SnmpSysObjectId { get; set; }
        public string SnmpLocation { get; set; }
        public string SnmpContact { get; set; }
        public string SnmpUptime { get; set; }
        public List<SnmpInterfaceInfo> SnmpInterfaces { get; set; } = new List<SnmpInterfaceInfo>();

        public XElement ToXml()
        {
            return new XElement("device",
                new XElement("Host", Host),
                new XElement("Alias", Alias),
                new XElement("Source", Source),
                new XElement("IsReachable", IsReachable),
                new XElement("CollectedUtc", CollectedUtc?.ToString("o")),
                new XElement("ErrorMessage", ErrorMessage),
                new XElement("Sistema",
                    new XElement("SerialNumber", SerialNumber),
                    new XElement("Manufacturer", Manufacturer),
                    new XElement("Model", Model),
                    new XElement("Uuid", Uuid),
                    new XElement("OsCaption", OsCaption),
                    new XElement("OsVersion", OsVersion),
                    new XElement("OsBuildNumber", OsBuildNumber),
                    new XElement("OsArchitecture", OsArchitecture),
                    new XElement("LastBootUpTime", LastBootUpTime),
                    new XElement("Domain", Domain),
                    new XElement("DnsHostname", DnsHostname),
                    new XElement("SystemType", SystemType)),
                new XElement("Hardware",
                    new XElement("CpuName", CpuName),
                    new XElement("CpuCores", CpuCores),
                    new XElement("CpuLogicalProcessors", CpuLogicalProcessors),
                    new XElement("TotalRamGB", TotalRamGB),
                    new XElement("RamSlots", RamSlots)),
                new XElement("Almacenamiento",
                    Disks.Select(d => new XElement("Disco",
                        new XElement("Label", d.Label),
                        new XElement("SizeGB", d.SizeGB),
                        new XElement("FreeGB", d.FreeGB)))),
                new XElement("Red",
                    new XElement("MacAddresses", MacAddresses),
                    new XElement("Ipv4", Ipv4),
                    new XElement("Ipv6", Ipv6),
                    new XElement("DhcpEnabled", DhcpEnabled)),
                new XElement("Snmp",
                    new XElement("SysName", SnmpSysName),
                    new XElement("SysDescr", SnmpSysDescr),
                    new XElement("SysObjectId", SnmpSysObjectId),
                    new XElement("Location", SnmpLocation),
                    new XElement("Contact", SnmpContact),
                    new XElement("Uptime", SnmpUptime),
                    new XElement("Interfaces",
                        SnmpInterfaces.Select(i => new XElement("Interface",
                            new XElement("Index", i.Index),
                            new XElement("Name", i.Name),
                            new XElement("Description", i.Description),
                            new XElement("MacAddress", i.MacAddress),
                            new XElement("IpAddress", i.IpAddress),
                            new XElement("InOctets", i.InOctets),
                            new XElement("OutOctets", i.OutOctets),
                            new XElement("Status", i.Status))))));
        }

        public static DeviceInventory FromXml(XElement element)
        {
            var device = new DeviceInventory
            {
                Host = (string)element.Element("Host"),
                Alias = (string)element.Element("Alias"),
                Source = (string)element.Element("Source"),
                IsReachable = (bool?)element.Element("IsReachable") ?? false,
                ErrorMessage = (string)element.Element("ErrorMessage")
            };

            var collected = (string)element.Element("CollectedUtc");
            if (DateTime.TryParse(collected, out var parsed))
            {
                device.CollectedUtc = parsed;
            }

            var sistema = element.Element("Sistema");
            if (sistema != null)
            {
                device.SerialNumber = (string)sistema.Element("SerialNumber");
                device.Manufacturer = (string)sistema.Element("Manufacturer");
                device.Model = (string)sistema.Element("Model");
                device.Uuid = (string)sistema.Element("Uuid");
                device.OsCaption = (string)sistema.Element("OsCaption");
                device.OsVersion = (string)sistema.Element("OsVersion");
                device.OsBuildNumber = (string)sistema.Element("OsBuildNumber");
                device.OsArchitecture = (string)sistema.Element("OsArchitecture");
                device.LastBootUpTime = (string)sistema.Element("LastBootUpTime");
                device.Domain = (string)sistema.Element("Domain");
                device.DnsHostname = (string)sistema.Element("DnsHostname");
                device.SystemType = (string)sistema.Element("SystemType");
            }

            var hardware = element.Element("Hardware");
            if (hardware != null)
            {
                device.CpuName = (string)hardware.Element("CpuName");
                device.CpuCores = (int?)hardware.Element("CpuCores") ?? 0;
                device.CpuLogicalProcessors = (int?)hardware.Element("CpuLogicalProcessors") ?? 0;
                device.TotalRamGB = (double?)hardware.Element("TotalRamGB") ?? 0;
                device.RamSlots = (int?)hardware.Element("RamSlots") ?? 0;
            }

            var almacenamiento = element.Element("Almacenamiento");
            if (almacenamiento != null)
            {
                device.Disks = almacenamiento.Elements("Disco")
                    .Select(d => new DiskInfo
                    {
                        Label = (string)d.Element("Label"),
                        SizeGB = (double?)d.Element("SizeGB") ?? 0,
                        FreeGB = (double?)d.Element("FreeGB") ?? 0
                    }).ToList();
            }

            var red = element.Element("Red");
            if (red != null)
            {
                device.MacAddresses = (string)red.Element("MacAddresses");
                device.Ipv4 = (string)red.Element("Ipv4");
                device.Ipv6 = (string)red.Element("Ipv6");
                device.DhcpEnabled = (string)red.Element("DhcpEnabled");
            }

            var snmp = element.Element("Snmp");
            if (snmp != null)
            {
                device.SnmpSysName = (string)snmp.Element("SysName");
                device.SnmpSysDescr = (string)snmp.Element("SysDescr");
                device.SnmpSysObjectId = (string)snmp.Element("SysObjectId");
                device.SnmpLocation = (string)snmp.Element("Location");
                device.SnmpContact = (string)snmp.Element("Contact");
                device.SnmpUptime = (string)snmp.Element("Uptime");
                var interfaces = snmp.Element("Interfaces");
                if (interfaces != null)
                {
                    device.SnmpInterfaces = interfaces.Elements("Interface")
                        .Select(i => new SnmpInterfaceInfo
                        {
                            Index = (int?)i.Element("Index") ?? 0,
                            Name = (string)i.Element("Name"),
                            Description = (string)i.Element("Description"),
                            MacAddress = (string)i.Element("MacAddress"),
                            IpAddress = (string)i.Element("IpAddress"),
                            InOctets = (long?)i.Element("InOctets") ?? 0,
                            OutOctets = (long?)i.Element("OutOctets") ?? 0,
                            Status = (string)i.Element("Status")
                        }).ToList();
                }
            }

            return device;
        }
    }

    public class DiskInfo
    {
        public string Label { get; set; }
        public double SizeGB { get; set; }
        public double FreeGB { get; set; }
    }

    public class SnmpInterfaceInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string MacAddress { get; set; }
        public string IpAddress { get; set; }
        public long InOctets { get; set; }
        public long OutOctets { get; set; }
        public string Status { get; set; }
    }
}
