using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace vmPing.Classes
{
    public class HealthProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public string CurrentHost { get; set; }
    }

    public class DiskHealth
    {
        public string Label { get; set; }
        public double SizeGB { get; set; }
        public double FreeGB { get; set; }
        public double UsedPercent { get; set; }
    }

    public class HealthSnapshot
    {
        public string Host { get; set; }
        public DateTime TimestampUtc { get; set; }
        public double CpuPercent { get; set; } = -1;
        public double RamPercent { get; set; } = -1;
        public double DiskPercent { get; set; } = -1;
        public List<DiskHealth> Disks { get; set; } = new List<DiskHealth>();
        public bool HasData => CpuPercent >= 0 || RamPercent >= 0 || DiskPercent >= 0;
        public string ErrorMessage { get; set; }
    }

    public static class HealthService
    {
        private const string WmiQueryCpu = "SELECT Name, LoadPercentage FROM Win32_Processor";
        private const string WmiQueryCpuPerf = "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name = '_Total'";
        private const string WmiQueryOs = "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem";
        private const string WmiQueryDisk = "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3";

        public static async Task<List<HealthSnapshot>> CollectAsync(
            IEnumerable<string> hosts,
            IProgress<HealthProgress> progress,
            CancellationToken cancellationToken)
        {
            var hostList = hosts.Distinct().Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
            var results = new ConcurrentBag<HealthSnapshot>();
            int completed = 0;

            using (var semaphore = new SemaphoreSlim(Math.Max(1, ApplicationOptions.InventoryConcurrency)))
            {
                var tasks = hostList.Select(async host =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        progress?.Report(new HealthProgress
                        {
                            Completed = completed,
                            Total = hostList.Count,
                            CurrentHost = host
                        });

                        var result = await Task.Run(() => CollectHost(host), cancellationToken).ConfigureAwait(false);
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        Interlocked.Increment(ref completed);
                        semaphore.Release();
                    }
                }).ToList();

                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new List<HealthSnapshot>();
                }
            }

            return results.OrderBy(r => r.Host, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static HealthSnapshot CollectHost(string host)
        {
            var snapshot = new HealthSnapshot
            {
                Host = host,
                TimestampUtc = DateTime.UtcNow
            };

            try
            {
                var options = new ConnectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(ApplicationOptions.InventoryTimeoutSeconds),
                    Authentication = AuthenticationLevel.PacketPrivacy
                };

                var username = ApplicationOptions.InventoryWmiUsername?.Trim();
                var domain = ApplicationOptions.InventoryWmiDomain?.Trim();
                if (!string.IsNullOrEmpty(username))
                {
                    options.Username = string.IsNullOrEmpty(domain) ? username : $"{domain}\\{username}";
                    options.Password = ApplicationOptions.InventoryWmiPassword;
                }

                var scope = new ManagementScope($@"\\{host}\root\cimv2", options);
                scope.Connect();
                if (!scope.IsConnected)
                {
                    snapshot.ErrorMessage = "No se pudo conectar por WMI.";
                    return snapshot;
                }

                // CPU usage: prefer the performance counter class (real-time), fallback to LoadPercentage.
                using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(WmiQueryCpuPerf)))
                {
                    var perf = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (perf != null)
                    {
                        var load = ToDouble(perf["PercentProcessorTime"]);
                        if (load >= 0)
                        {
                            snapshot.CpuPercent = Math.Round(load, 1);
                        }
                    }
                }
                if (snapshot.CpuPercent < 0)
                {
                    using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(WmiQueryCpu)))
                    {
                        var cpu = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                        if (cpu != null)
                        {
                            var load = ToDouble(cpu["LoadPercentage"]);
                            if (load >= 0)
                            {
                                snapshot.CpuPercent = Math.Round(load, 1);
                            }
                        }
                    }
                }

                // RAM usage.
                using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(WmiQueryOs)))
                {
                    var os = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (os != null)
                    {
                        var totalKb = ToDouble(os["TotalVisibleMemorySize"]);
                        var freeKb = ToDouble(os["FreePhysicalMemory"]);
                        if (totalKb > 0)
                        {
                            snapshot.RamPercent = Math.Round((totalKb - freeKb) / totalKb * 100.0, 1);
                        }
                    }
                }

                // Disk usage per local drive.
                using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(WmiQueryDisk)))
                {
                    foreach (var disk in searcher.Get().Cast<ManagementObject>())
                    {
                        var sizeBytes = ToDouble(disk["Size"]);
                        if (sizeBytes <= 0)
                        {
                            continue;
                        }
                        var freeBytes = ToDouble(disk["FreeSpace"]);
                        var usedPercent = (sizeBytes - freeBytes) / sizeBytes * 100.0;
                        snapshot.Disks.Add(new DiskHealth
                        {
                            Label = Clean(disk["DeviceID"]),
                            SizeGB = Math.Round(sizeBytes / 1024.0 / 1024.0 / 1024.0, 1),
                            FreeGB = Math.Round(freeBytes / 1024.0 / 1024.0 / 1024.0, 1),
                            UsedPercent = Math.Round(usedPercent, 1)
                        });
                    }
                    if (snapshot.Disks.Count > 0)
                    {
                        snapshot.DiskPercent = snapshot.Disks.Max(d => d.UsedPercent);
                    }
                }

                if (!snapshot.HasData)
                {
                    snapshot.ErrorMessage = "Sin datos disponibles.";
                }
            }
            catch
            {
                snapshot.ErrorMessage = "No se pudo obtener información. Verifique que el equipo esté encendido y accesible, y que las credenciales WMI sean correctas.";
            }

            return snapshot;
        }

        private static string Clean(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return null;
            }
            var text = value.ToString().Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static double ToDouble(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return -1;
            }
            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return -1;
            }
        }
    }
}
