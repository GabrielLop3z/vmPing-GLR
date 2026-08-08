using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace vmPing.Classes
{
    public class DnsRecordResult
    {
        public string RecordType { get; set; }
        public string Value { get; set; }
        public string RawOutput { get; set; }
    }

    public static class DnsQuery
    {
        public static async Task<List<DnsRecordResult>> QueryAsync(string domainOrIp, string recordType)
        {
            var results = new List<DnsRecordResult>();
            recordType = recordType?.ToUpperInvariant() ?? "A";

            if (string.IsNullOrWhiteSpace(domainOrIp))
                return results;

            domainOrIp = domainOrIp.Trim();

            // Direct System.Net.Dns attempt for A / AAAA / PTR
            if (recordType == "A" || recordType == "AAAA" || recordType == "ALL")
            {
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(domainOrIp);
                    foreach (var ip in addresses)
                    {
                        if (recordType == "A" && ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            results.Add(new DnsRecordResult { RecordType = "A", Value = ip.ToString() });
                        }
                        else if (recordType == "AAAA" && ip.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            results.Add(new DnsRecordResult { RecordType = "AAAA", Value = ip.ToString() });
                        }
                        else if (recordType == "ALL")
                        {
                            string typeName = ip.AddressFamily == AddressFamily.InterNetwork ? "A" : "AAAA";
                            results.Add(new DnsRecordResult { RecordType = typeName, Value = ip.ToString() });
                        }
                    }
                }
                catch { /* fallback to nslookup */ }
            }

            if (recordType == "PTR" || IPAddress.TryParse(domainOrIp, out _))
            {
                try
                {
                    if (IPAddress.TryParse(domainOrIp, out IPAddress ip))
                    {
                        var entry = await Dns.GetHostEntryAsync(ip);
                        if (!string.IsNullOrEmpty(entry.HostName))
                        {
                            results.Add(new DnsRecordResult { RecordType = "PTR", Value = entry.HostName });
                        }
                    }
                }
                catch { /* fallback to nslookup */ }
            }

            // If no results yet, or for MX / TXT / NS / ALL, query via nslookup process
            if (results.Count == 0 || recordType == "MX" || recordType == "TXT" || recordType == "NS" || recordType == "ALL")
            {
                var nslookupResults = await QueryNslookupAsync(domainOrIp, recordType == "ALL" ? "ANY" : recordType);
                foreach (var r in nslookupResults)
                {
                    // Avoid duplicates
                    if (!results.Exists(x => x.RecordType == r.RecordType && x.Value == r.Value))
                    {
                        results.Add(r);
                    }
                }
            }

            return results;
        }

        private static Task<List<DnsRecordResult>> QueryNslookupAsync(string domain, string type)
        {
            return Task.Run(() =>
            {
                var list = new List<DnsRecordResult>();
                try
                {
                    var psi = new ProcessStartInfo("nslookup")
                    {
                        Arguments = $"-type={type} {domain}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var proc = Process.Start(psi))
                    {
                        if (proc != null)
                        {
                            string output = proc.StandardOutput.ReadToEnd();
                            proc.WaitForExit(5000);

                            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            bool passedServerHeader = false;

                            foreach (var line in lines)
                            {
                                string trimmed = line.Trim();
                                if (trimmed.StartsWith("Server:") || trimmed.StartsWith("Address:"))
                                {
                                    passedServerHeader = true;
                                    continue;
                                }

                                if (!passedServerHeader) continue;

                                if (trimmed.Contains("=")|| trimmed.StartsWith("Name:") || trimmed.Contains("mail exchanger") || trimmed.Contains("text ="))
                                {
                                    list.Add(new DnsRecordResult
                                    {
                                        RecordType = type,
                                        Value = trimmed,
                                        RawOutput = output
                                    });
                                }
                                else if (trimmed.StartsWith("Addresses:") || trimmed.StartsWith("Address:"))
                                {
                                    var parts = trimmed.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var p in parts)
                                    {
                                        if (IPAddress.TryParse(p, out _))
                                        {
                                            list.Add(new DnsRecordResult { RecordType = type, Value = p, RawOutput = output });
                                        }
                                    }
                                }
                            }

                            if (list.Count == 0 && !string.IsNullOrWhiteSpace(output))
                            {
                                list.Add(new DnsRecordResult { RecordType = type, Value = output.Trim(), RawOutput = output });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    list.Add(new DnsRecordResult { RecordType = "Error", Value = ex.Message });
                }

                return list;
            });
        }
    }
}
