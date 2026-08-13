using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Globalization;
using System.Linq;

namespace vmPing.Classes
{
    public enum AdAccountStatus
    {
        Enabled,
        Disabled,
        Unknown
    }

    public class AdComputer
    {
        public string Name { get; set; }
        public string DnsHostName { get; set; }
        public string OperatingSystem { get; set; }
        public string OsVersion { get; set; }
        public string ServicePack { get; set; }
        public string Description { get; set; }
        public string OuPath { get; set; }
        public string DistinguishedName { get; set; }
        public DateTime? LastLogon { get; set; }
        public long LogonCount { get; set; }
        public DateTime? WhenCreated { get; set; }
        public AdAccountStatus Status { get; set; }

        public string StatusText => Status == AdAccountStatus.Enabled ? "Habilitada" : Status == AdAccountStatus.Disabled ? "Deshabilitada" : "?";
        public string LastLogonText => LastLogon?.ToString("yyyy-MM-dd HH:mm") ?? "-";
        public string WhenCreatedText => WhenCreated?.ToString("yyyy-MM-dd") ?? "-";

        // Correlated WMI inventory (from inventory.xml).
        public DeviceInventory Inventory { get; set; }
        public bool HasInventory => Inventory != null;
        public string Serials => Inventory?.SerialNumber ?? "-";
        public string Model => Inventory?.Model ?? "-";
        public string Manufacturer => Inventory?.Manufacturer ?? "-";
        public string RamText => Inventory != null && Inventory.TotalRamGB > 0 ? $"{Inventory.TotalRamGB:0.#} GB" : "-";
        public string MacText => Inventory?.MacAddresses ?? "-";
        public string IpText => Inventory?.Ipv4 ?? "-";
        public string InventoryStateText => HasInventory ? (Inventory.IsReachable ? "Disponible" : "Sin datos") : "No inventariado";
    }

    public class AdUser
    {
        public string Name { get; set; }
        public string SamAccountName { get; set; }
        public string DisplayName { get; set; }
        public string OuPath { get; set; }
        public DateTime? LastLogon { get; set; }
        public string Description { get; set; }
        public AdAccountStatus Status { get; set; }
        public string StatusText => Status == AdAccountStatus.Enabled ? "Habilitado" : Status == AdAccountStatus.Disabled ? "Deshabilitado" : "?";
        public string LastLogonText => LastLogon?.ToString("yyyy-MM-dd HH:mm") ?? "-";
    }

    public static class AdService
    {
        private const int UAC_ACCOUNTDISABLE = 0x0002;
        private const int PCREASELIMIT = 500;

        public static string DefaultLdapPath()
        {
            return "LDAP://" + (string.IsNullOrWhiteSpace(Environment.UserDomainName) ? "" : Environment.UserDomainName);
        }

        public static string GetLdapPath()
        {
            var configured = ApplicationOptions.AdLdapPath?.Trim();
            return string.IsNullOrEmpty(configured) ? DefaultLdapPath() : configured;
        }

        public static DirectoryEntry CreateRootEntry()
        {
            var path = GetLdapPath();
            var username = ApplicationOptions.AdUsername?.Trim();
            if (!string.IsNullOrEmpty(username))
            {
                return new DirectoryEntry(path, username, ApplicationOptions.AdPassword,
                    AuthenticationTypes.Secure | AuthenticationTypes.ServerBind);
            }
            return new DirectoryEntry(path);
        }

        // Converts a Windows FILETIME (100ns ticks since 1601) stored in large integers to DateTime?.
        private static DateTime? FromLargeInt(object value)
        {
            if (value == null || value.ToString() == "0")
            {
                return null;
            }
            try
            {
                long ticks = Convert.ToInt64(value.ToString(), CultureInfo.InvariantCulture);
                if (ticks <= 0)
                {
                    return null;
                }
                return DateTime.FromFileTime(ticks).ToLocalTime();
            }
            catch
            {
                return null;
            }
        }

        public static string ExtractOuPath(string distinguishedName)
        {
            if (string.IsNullOrEmpty(distinguishedName))
            {
                return string.Empty;
            }
            var parts = distinguishedName.Split(',');
            return string.Join("/", parts
                .Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .Select(p => p.Substring(3)));
        }

        private static AdAccountStatus ParseStatus(object userAccountControl)
        {
            if (userAccountControl == null)
            {
                return AdAccountStatus.Unknown;
            }
            try
            {
                long uac = Convert.ToInt64(userAccountControl.ToString(), CultureInfo.InvariantCulture);
                return (uac & UAC_ACCOUNTDISABLE) != 0 ? AdAccountStatus.Disabled : AdAccountStatus.Enabled;
            }
            catch
            {
                return AdAccountStatus.Unknown;
            }
        }

        public static List<AdComputer> QueryComputers()
        {
            var computers = new List<AdComputer>();
            using (var root = CreateRootEntry())
            using (var searcher = new DirectorySearcher(root))
            {
                searcher.Filter = "(objectClass=computer)";
                searcher.PageSize = PCREASELIMIT;
                searcher.PropertiesToLoad.AddRange(new[]
                {
                    "cn", "dnshostname", "operatingsystem", "operatingsystemversion",
                    "operatingsystemservicepack", "description", "distinguishedname",
                    "lastlogontimestamp", "logoncount", "whencreated", "useraccountcontrol"
                });
                searcher.ServerTimeLimit = TimeSpan.FromSeconds(ApplicationOptions.AdTimeoutSeconds);
                searcher.ServerPageTimeLimit = TimeSpan.FromSeconds(ApplicationOptions.AdTimeoutSeconds);

                foreach (SearchResult result in searcher.FindAll())
                {
                    var p = result.Properties;
                    var dn = StringValue(p, "distinguishedname");
                    computers.Add(new AdComputer
                    {
                        Name = StringValue(p, "cn"),
                        DnsHostName = StringValue(p, "dnshostname"),
                        OperatingSystem = StringValue(p, "operatingsystem"),
                        OsVersion = StringValue(p, "operatingsystemversion"),
                        ServicePack = StringValue(p, "operatingsystemservicepack"),
                        Description = StringValue(p, "description"),
                        OuPath = ExtractOuPath(dn),
                        DistinguishedName = dn,
                        LastLogon = FromLargeInt(p["lastlogontimestamp"][0]),
                        LogonCount = LongValue(p, "logoncount"),
                        WhenCreated = FromLargeInt(p["whencreated"][0]),
                        Status = ParseStatus(p["useraccountcontrol"][0])
                    });
                }
            }
            return computers;
        }

        public static List<AdUser> QueryEnabledUsers()
        {
            var users = new List<AdUser>();
            using (var root = CreateRootEntry())
            using (var searcher = new DirectorySearcher(root))
            {
                searcher.Filter = "(&(objectClass=user)(objectCategory=person))";
                searcher.PageSize = PCREASELIMIT;
                searcher.PropertiesToLoad.AddRange(new[]
                {
                    "cn", "samaccountname", "displayname", "distinguishedname",
                    "lastlogontimestamp", "description", "useraccountcontrol"
                });
                searcher.ServerTimeLimit = TimeSpan.FromSeconds(ApplicationOptions.AdTimeoutSeconds);
                searcher.ServerPageTimeLimit = TimeSpan.FromSeconds(ApplicationOptions.AdTimeoutSeconds);

                foreach (SearchResult result in searcher.FindAll())
                {
                    var p = result.Properties;
                    users.Add(new AdUser
                    {
                        Name = StringValue(p, "cn"),
                        SamAccountName = StringValue(p, "samaccountname"),
                        DisplayName = StringValue(p, "displayname"),
                        OuPath = ExtractOuPath(StringValue(p, "distinguishedname")),
                        LastLogon = FromLargeInt(p["lastlogontimestamp"][0]),
                        Description = StringValue(p, "description"),
                        Status = ParseStatus(p["useraccountcontrol"][0])
                    });
                }
            }
            return users;
        }

        // Correlates AD computers with the cached WMI inventory (inventory.xml).
        public static void CorrelateWithInventory(List<AdComputer> computers, List<DeviceInventory> inventory)
        {
            if (computers == null || inventory == null)
            {
                return;
            }
            var inventoryByKey = inventory.ToDictionary(i => NormalizeName(i.Host), StringComparer.OrdinalIgnoreCase);
            foreach (var computer in computers)
            {
                var hostKey = NormalizeName(computer.DnsHostName);
                if (hostKey == null) hostKey = NormalizeName(computer.Name);
                if (hostKey != null && inventoryByKey.TryGetValue(hostKey, out var device))
                {
                    computer.Inventory = device;
                }
            }
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            var host = value.Split('.')[0];
            host = host.Replace("$", "").Trim();
            return host.Length == 0 ? null : host;
        }

        private static string StringValue(ResultPropertyCollection p, string key)
        {
            if (p[key] == null || p[key].Count == 0)
            {
                return null;
            }
            var value = p[key][0].ToString();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static long LongValue(ResultPropertyCollection p, string key)
        {
            if (p[key] == null || p[key].Count == 0)
            {
                return 0;
            }
            long result = 0;
            long.TryParse(p[key][0].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            return result;
        }

        public static List<DeviceInventory> LoadInventoryFromDisk()
        {
            return InventoryStore.Load();
        }
    }
}