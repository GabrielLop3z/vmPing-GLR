using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace vmPing.Classes
{
    public class InventoryProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public string CurrentHost { get; set; }
        public string Message { get; set; }
    }

    public static class InventoryService
    {
        // SNMP system OIDs.
        private static readonly ObjectIdentifier OidSysDescr = new ObjectIdentifier(".1.3.6.1.2.1.1.1.0");
        private static readonly ObjectIdentifier OidSysObjectId = new ObjectIdentifier(".1.3.6.1.2.1.1.2.0");
        private static readonly ObjectIdentifier OidSysUpTime = new ObjectIdentifier(".1.3.6.1.2.1.1.3.0");
        private static readonly ObjectIdentifier OidSysContact = new ObjectIdentifier(".1.3.6.1.2.1.1.4.0");
        private static readonly ObjectIdentifier OidSysName = new ObjectIdentifier(".1.3.6.1.2.1.1.5.0");
        private static readonly ObjectIdentifier OidSysLocation = new ObjectIdentifier(".1.3.6.1.2.1.1.6.0");
        private static readonly ObjectIdentifier OidIfTable = new ObjectIdentifier(".1.3.6.1.2.1.2.2.1");

        public static async Task<List<DeviceInventory>> CollectAsync(
            IEnumerable<string> hosts,
            IProgress<InventoryProgress> progress,
            CancellationToken cancellationToken)
        {
            var hostList = hosts.Distinct().Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
            var results = new ConcurrentBag<DeviceInventory>();
            int completed = 0;

            using (var semaphore = new SemaphoreSlim(Math.Max(1, ApplicationOptions.InventoryConcurrency)))
            {
                var tasks = hostList.Select(async host =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        progress?.Report(new InventoryProgress
                        {
                            Completed = completed,
                            Total = hostList.Count,
                            CurrentHost = host,
                            Message = "Consultando..."
                        });

                        var result = await Task.Run(() => CollectHost(host), cancellationToken).ConfigureAwait(false);
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancellation requested, stop processing.
                    }
                    finally
                    {
                        Interlocked.Increment(ref completed);
                        semaphore.Release();
                        progress?.Report(new InventoryProgress
                        {
                            Completed = completed,
                            Total = hostList.Count,
                            CurrentHost = host,
                            Message = "Finalizado"
                        });
                    }
                }).ToList();

                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation requested, discard partial results.
                    return new List<DeviceInventory>();
                }
            }

            return results.OrderBy(r => r.Host, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static DeviceInventory CollectHost(string host)
        {
            var inventory = new DeviceInventory
            {
                Host = host,
                Source = "Sin datos",
                CollectedUtc = DateTime.UtcNow
            };

            var aliases = Alias.GetAll();
            if (host != null && aliases.ContainsKey(host.ToLower()))
            {
                inventory.Alias = aliases[host.ToLower()];
            }

            bool snmpOk = false;

            if (ApplicationOptions.InventorySnmpEnabled)
            {
                snmpOk = CollectSnmp(host, inventory);
            }

            inventory.IsReachable = snmpOk;
            inventory.Source = snmpOk ? "SNMP" : "Sin datos";

            if (!inventory.IsReachable)
            {
                inventory.ErrorMessage = "No se pudo obtener informacion. Verifique que el equipo este encendido y accesible, y que la community SNMP sea correcta.";
            }

            return inventory;
        }

        #region SNMP

        private static bool CollectSnmp(string host, DeviceInventory inventory)
        {
            try
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(host), ApplicationOptions.InventorySnmpPort);
                var community = new OctetString(ApplicationOptions.InventorySnmpCommunity ?? "public");
                var timeout = ApplicationOptions.InventoryTimeoutSeconds * 1000;

                var variables = new List<Variable>
                {
                    new Variable(OidSysName),
                    new Variable(OidSysDescr),
                    new Variable(OidSysObjectId),
                    new Variable(OidSysUpTime),
                    new Variable(OidSysContact),
                    new Variable(OidSysLocation)
                };

                var reply = Messenger.Get(VersionCode.V2, endpoint, community, variables, timeout);
                var map = reply.ToDictionary(v => v.Id.ToString(), v => v.Data);

                inventory.SnmpSysName = Decode(map, OidSysName.ToString());
                inventory.SnmpSysDescr = Decode(map, OidSysDescr.ToString());
                inventory.SnmpSysObjectId = Decode(map, OidSysObjectId.ToString());
                inventory.SnmpUptime = Decode(map, OidSysUpTime.ToString());
                inventory.SnmpContact = Decode(map, OidSysContact.ToString());
                inventory.SnmpLocation = Decode(map, OidSysLocation.ToString());

                CollectSnmpInterfaces(endpoint, community, timeout, inventory);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CollectSnmpInterfaces(IPEndPoint endpoint, OctetString community, int timeout, DeviceInventory inventory)
        {
            try
            {
                var table = Messenger.GetTable(VersionCode.V2, endpoint, community, OidIfTable, timeout, 20);

                var indexes = new List<int>();
                var descriptions = new Dictionary<int, string>();
                var names = new Dictionary<int, string>();
                var macs = new Dictionary<int, string>();
                var statuses = new Dictionary<int, string>();
                var inOctets = new Dictionary<int, long>();
                var outOctets = new Dictionary<int, long>();

                for (int row = 0; row < table.GetLength(0); row++)
                {
                    for (int col = 0; col < table.GetLength(1); col++)
                    {
                        var variable = table[row, col];
                        if (variable == null)
                        {
                            continue;
                        }

                        var parts = variable.Id.ToString().Split('.');
                        if (parts.Length < 2 || !int.TryParse(parts[parts.Length - 1], out int index))
                        {
                            continue;
                        }

                        if (!indexes.Contains(index))
                        {
                            indexes.Add(index);
                        }

                        var oid = variable.Id.ToString();
                        if (oid.EndsWith(".2." + index)) descriptions[index] = Decode(variable.Data);
                        else if (oid.EndsWith(".1." + index)) names[index] = index.ToString();
                        else if (oid.EndsWith(".6." + index)) macs[index] = FormatMac(Decode(variable.Data));
                        else if (oid.EndsWith(".8." + index)) statuses[index] = Decode(variable.Data);
                        else if (oid.EndsWith(".10." + index)) inOctets[index] = ToLong(variable.Data);
                        else if (oid.EndsWith(".16." + index)) outOctets[index] = ToLong(variable.Data);
                    }
                }

                foreach (var index in indexes.OrderBy(i => i))
                {
                    inventory.SnmpInterfaces.Add(new SnmpInterfaceInfo
                    {
                        Index = index,
                        Name = names.TryGetValue(index, out var name) ? name : index.ToString(),
                        Description = descriptions.TryGetValue(index, out var desc) ? desc : string.Empty,
                        MacAddress = macs.TryGetValue(index, out var mac) ? mac : string.Empty,
                        Status = statuses.TryGetValue(index, out var status) ? status : string.Empty,
                        InOctets = inOctets.TryGetValue(index, out var inO) ? inO : 0,
                        OutOctets = outOctets.TryGetValue(index, out var outO) ? outO : 0
                    });
                }
            }
            catch
            {
                // Interfaces are optional; don't fail the whole device.
            }
        }

        #endregion

        #region Helpers

        private static string Clean(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return null;
            }
            var text = value.ToString().Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static int ToInt(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return 0;
            }
            return Convert.ToInt32(value);
        }

        private static double ToDouble(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return 0;
            }
            return Convert.ToDouble(value);
        }

        private static string[] ToStringArray(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return Array.Empty<string>();
            }
            if (value is string[] array)
            {
                return array;
            }
            return new[] { value.ToString() };
        }

        private static string Decode(IDictionary<string, ISnmpData> map, string oid)
        {
            return map.TryGetValue(oid, out var data) ? Decode(data) : null;
        }

        private static string Decode(ISnmpData data)
        {
            if (data == null)
            {
                return null;
            }
            return data.ToString();
        }

        private static long ToLong(ISnmpData data)
        {
            if (data == null)
            {
                return 0;
            }
            var counter = data as Counter32;
            if (counter != null)
            {
                return counter.ToUInt32();
            }
            var counter64 = data as Counter64;
            if (counter64 != null)
            {
                return (long)counter64.ToUInt64();
            }
            var gauge = data as Gauge32;
            if (gauge != null)
            {
                return gauge.ToUInt32();
            }
            var integer = data as Integer32;
            if (integer != null)
            {
                return integer.ToInt32();
            }
            return long.TryParse(data.ToString(), out var result) ? result : 0;
        }

        private static string FormatMac(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            // SharpSnmpLib renders OctetString as ASCII; hex MACs come through as control chars.
            var bytes = raw.Trim('\0').Select(c => (byte)c).ToArray();
            if (bytes.Length == 6)
            {
                return string.Join(":", bytes.Select(b => b.ToString("X2")));
            }
            return raw;
        }

        #endregion
    }

    public static class InventoryCache
    {
        private static readonly ConcurrentDictionary<string, DeviceInventory> Cache =
            new ConcurrentDictionary<string, DeviceInventory>(StringComparer.OrdinalIgnoreCase);

        public static DeviceInventory Get(string host)
        {
            if (host == null)
            {
                return null;
            }
            if (Cache.TryGetValue(host, out var device) &&
                device.CollectedUtc.HasValue &&
                (DateTime.UtcNow - device.CollectedUtc.Value).TotalMinutes < 5)
            {
                return device;
            }
            return null;
        }

        public static void Set(DeviceInventory device)
        {
            if (device?.Host != null)
            {
                Cache[device.Host] = device;
            }
        }
    }
}
