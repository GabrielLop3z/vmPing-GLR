using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace vmPing.Classes
{
    /// <summary>
    /// Instantánea de un host monitoreado, construida por la ventana principal en el hilo de UI.
    /// </summary>
    public class ApiHostSnapshot
    {
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public string Status { get; set; }
        public bool IsActive { get; set; }
        public long Sent { get; set; }
        public long Received { get; set; }
        public long Lost { get; set; }
        public long Error { get; set; }
        public long MinRttMs { get; set; }
        public double AvgRttMs { get; set; }
        public long MaxRttMs { get; set; }
        public long? CurrentRttMs { get; set; }
    }

    /// <summary>
    /// API HTTP local de solo lectura para integrar vmPing con bitácoras y otras aplicaciones.
    /// Usa TcpListener (sin reservas URL ACL ni permisos de administrador).
    /// </summary>
    public static class ApiService
    {
        private sealed class ApiResponse
        {
            public int Status { get; set; } = 200;
            public string ContentType { get; set; } = "application/json; charset=utf-8";
            public string Body { get; set; } = "{}";
        }

        private static readonly object Sync = new object();
        private static readonly SemaphoreSlim ClientLimit = new SemaphoreSlim(32, 32);

        private static TcpListener _listener;
        private static CancellationTokenSource _cancellation;
        private static DateTime _startedUtc;
        private static string _lastError = string.Empty;
        private static int _activePort;
        private static bool _activeLan;

        public static Func<IReadOnlyList<ApiHostSnapshot>> HostSnapshotProvider { get; set; }

        public static bool IsRunning
        {
            get
            {
                lock (Sync)
                {
                    return _listener != null;
                }
            }
        }

        public static int Port
        {
            get
            {
                lock (Sync)
                {
                    return _activePort;
                }
            }
        }

        public static bool IsListeningOnLan
        {
            get
            {
                lock (Sync)
                {
                    return _activeLan;
                }
            }
        }

        public static string LastError
        {
            get
            {
                lock (Sync)
                {
                    return _lastError;
                }
            }
        }

        public static string BaseUrl => $"http://{(IsListeningOnLan ? GetLanAddress() : "127.0.0.1")}:{Port}";

        public static string LocalUrl => $"http://127.0.0.1:{Port}";

        public static bool Start()
        {
            Stop();

            lock (Sync)
            {
                _lastError = string.Empty;
            }

            if (!ApplicationOptions.ApiEnabled)
            {
                return false;
            }

            var port = ApplicationOptions.ApiPort;
            if (port < 1024 || port > 65535)
            {
                SetError("El puerto de la API debe estar entre 1024 y 65535.");
                return false;
            }

            if (ApplicationOptions.ApiListenLan && string.IsNullOrWhiteSpace(ApplicationOptions.ApiKey))
            {
                SetError("Para escuchar en la red local debe configurar una clave API.");
                return false;
            }

            try
            {
                var address = ApplicationOptions.ApiListenLan ? IPAddress.Any : IPAddress.Loopback;
                var listener = new TcpListener(address, port);
                listener.Start();

                var cancellation = new CancellationTokenSource();

                lock (Sync)
                {
                    _listener = listener;
                    _cancellation = cancellation;
                    _activePort = port;
                    _activeLan = ApplicationOptions.ApiListenLan;
                    _startedUtc = DateTime.UtcNow;
                }

                Task.Run(() => AcceptLoop(listener, cancellation.Token));

                HistoryService.RecordAudit(
                    "api-start",
                    "vmPing",
                    $"bind={(ApplicationOptions.ApiListenLan ? "0.0.0.0" : "127.0.0.1")};port={port}");

                return true;
            }
            catch (Exception ex)
            {
                SetError($"No se pudo iniciar la API: {ex.Message}");
                Stop();
                return false;
            }
        }

        public static void Stop()
        {
            TcpListener listener;
            CancellationTokenSource cancellation;
            var wasRunning = false;

            lock (Sync)
            {
                listener = _listener;
                cancellation = _cancellation;
                _listener = null;
                _cancellation = null;
                wasRunning = listener != null;
                _activePort = 0;
                _activeLan = false;
            }

            try
            {
                cancellation?.Cancel();
            }
            catch
            {
                // Best effort.
            }

            try
            {
                listener?.Stop();
            }
            catch
            {
                // Best effort.
            }

            if (wasRunning)
            {
                HistoryService.RecordAudit("api-stop", "vmPing", string.Empty);
            }
        }

        public static bool Restart()
        {
            Stop();
            return Start();
        }

        private static void SetError(string message)
        {
            lock (Sync)
            {
                _lastError = message ?? string.Empty;
            }
        }

        private static string GetLanAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var address = host.AddressList.FirstOrDefault(a =>
                    a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                if (address != null)
                {
                    return address.ToString();
                }
            }
            catch
            {
                // Best effort.
            }

            return "127.0.0.1";
        }

        private static async Task AcceptLoop(TcpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
                catch
                {
                    break;
                }

                if (token.IsCancellationRequested)
                {
                    client.Close();
                    break;
                }

                if (!ClientLimit.Wait(0))
                {
                    client.Close();
                    continue;
                }

                var captured = client;
                _ = Task.Run(() =>
                {
                    try
                    {
                        HandleClient(captured);
                    }
                    finally
                    {
                        ClientLimit.Release();
                    }
                });
            }
        }

        private static void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;

                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
                    {
                        var requestLine = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(requestLine) || requestLine.Length > 4096)
                        {
                            return;
                        }

                        var requestParts = requestLine.Split(' ');
                        if (requestParts.Length < 2)
                        {
                            return;
                        }

                        var method = requestParts[0].ToUpperInvariant();
                        var rawTarget = requestParts[1];

                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var headerCount = 0;
                        string headerLine;
                        while (!string.IsNullOrEmpty(headerLine = reader.ReadLine()))
                        {
                            if (++headerCount > 100 || headerLine.Length > 8192)
                            {
                                return;
                            }

                            var separator = headerLine.IndexOf(':');
                            if (separator <= 0)
                            {
                                continue;
                            }

                            headers[headerLine.Substring(0, separator).Trim()] =
                                headerLine.Substring(separator + 1).Trim();
                        }

                        string path;
                        string query;
                        SplitTarget(rawTarget, out path, out query);

                        // El preflight CORS del navegador no incluye la clave; debe responderse antes de autenticar.
                        if (method == "OPTIONS")
                        {
                            WriteResponse(
                                stream,
                                false,
                                new ApiResponse
                                {
                                    Status = 204,
                                    ContentType = "text/plain; charset=utf-8",
                                    Body = string.Empty
                                });
                            return;
                        }

                        if (!IsAuthorized(headers, query))
                        {
                            WriteResponse(
                                stream,
                                method == "HEAD",
                                new ApiResponse
                                {
                                    Status = 401,
                                    Body = Json.Serialize(new Dictionary<string, object>
                                    {
                                        { "error", "No autorizado. Envíe la cabecera X-Api-Key." }
                                    })
                                });
                            return;
                        }

                        if (method != "GET" && method != "HEAD")
                        {
                            WriteResponse(
                                stream,
                                method == "HEAD",
                                new ApiResponse
                                {
                                    Status = 405,
                                    Body = Json.Serialize(new Dictionary<string, object>
                                    {
                                        { "error", "Método no permitido. Use GET." }
                                    })
                                });
                            return;
                        }

                        var response = Route(path, query);
                        WriteResponse(stream, method == "HEAD", response);
                    }
                }
            }
            catch
            {
                // Un cliente defectuoso no debe afectar al resto.
            }
        }

        private static void SplitTarget(string rawTarget, out string path, out string query)
        {
            path = rawTarget ?? "/";
            query = string.Empty;

            if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            {
                query = absolute.Query.TrimStart('?');
                path = absolute.AbsolutePath;
            }
            else
            {
                var index = path.IndexOf('?');
                if (index >= 0)
                {
                    query = path.Substring(index + 1);
                    path = path.Substring(0, index);
                }
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }
        }

        private static bool IsAuthorized(Dictionary<string, string> headers, string query)
        {
            var expected = ApplicationOptions.ApiKey;
            if (string.IsNullOrWhiteSpace(expected))
            {
                return true;
            }

            string provided;
            headers.TryGetValue("X-Api-Key", out provided);

            if (string.IsNullOrWhiteSpace(provided) &&
                headers.TryGetValue("Authorization", out var authorization) &&
                authorization != null &&
                authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                provided = authorization.Substring(7).Trim();
            }

            if (string.IsNullOrWhiteSpace(provided))
            {
                var queryValues = ParseQuery(query);
                if (queryValues.TryGetValue("key", out var key))
                {
                    provided = key;
                }
                else if (queryValues.TryGetValue("apikey", out var apiKey))
                {
                    provided = apiKey;
                }
            }

            return string.Equals(provided, expected, StringComparison.Ordinal);
        }

        private static ApiResponse Route(string path, string query)
        {
            var values = ParseQuery(query);
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                return new ApiResponse
                {
                    ContentType = "text/html; charset=utf-8",
                    Body = BuildLandingPage()
                };
            }

            if (!string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase))
            {
                return Error(404, "Ruta no encontrada. Use /api.");
            }

            if (segments.Length == 1)
            {
                return Ok(BuildApiIndex());
            }

            var resource = segments[1].ToLowerInvariant();

            switch (resource)
            {
                case "health":
                    return Ok(BuildHealth());

                case "hosts":
                    if (segments.Length == 2)
                    {
                        return Ok(BuildHosts());
                    }

                    if (segments.Length >= 3)
                    {
                        var host = Uri.UnescapeDataString(segments[2]);
                        return BuildHostDetail(host, values);
                    }

                    break;

                case "ping":
                    return BuildPing(values);

                case "samples":
                    return Ok(BuildSamples(values));

                case "events":
                    return Ok(BuildEvents(values));

                case "stats":
                    return Ok(BuildStats(values));

                case "daily":
                    return BuildDaily(values);

                case "inventory":
                    return Ok(BuildInventory(values));

                case "openapi.json":
                    return new ApiResponse
                    {
                        ContentType = "application/json; charset=utf-8",
                        Body = OpenApiSpec
                    };
            }

            return Error(404, "Ruta no encontrada.");
        }

        private static ApiResponse Ok(object payload)
        {
            return new ApiResponse { Body = Json.Serialize(payload) };
        }

        private static ApiResponse Error(int status, string message)
        {
            return new ApiResponse
            {
                Status = status,
                Body = Json.Serialize(new Dictionary<string, object> { { "error", message } })
            };
        }

        private static void WriteResponse(NetworkStream stream, bool headOnly, ApiResponse response)
        {
            var body = Encoding.UTF8.GetBytes(response.Body ?? string.Empty);

            var header = new StringBuilder();
            header.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(Reason(response.Status)).Append("\r\n");
            header.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
            header.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            header.Append("Connection: close\r\n");
            header.Append("Cache-Control: no-store\r\n");
            header.Append("X-Content-Type-Options: nosniff\r\n");

            if (ApplicationOptions.ApiCorsEnabled)
            {
                header.Append("Access-Control-Allow-Origin: *\r\n");
                header.Append("Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\n");
                header.Append("Access-Control-Allow-Headers: X-Api-Key, Authorization, Content-Type\r\n");
            }

            header.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);

            if (!headOnly && body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 500: return "Internal Server Error";
                default: return "Status";
            }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
            {
                return result;
            }

            foreach (var pair in query.Split('&'))
            {
                if (string.IsNullOrWhiteSpace(pair))
                {
                    continue;
                }

                var index = pair.IndexOf('=');
                var name = index >= 0 ? pair.Substring(0, index) : pair;
                var value = index >= 0 ? pair.Substring(index + 1) : string.Empty;

                try
                {
                    name = Uri.UnescapeDataString(name.Replace('+', ' '));
                    value = Uri.UnescapeDataString(value.Replace('+', ' '));
                }
                catch
                {
                    // Keep raw values.
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    result[name] = value;
                }
            }

            return result;
        }

        private static string GetString(Dictionary<string, string> values, params string[] names)
        {
            foreach (var name in names)
            {
                if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static int GetInt(Dictionary<string, string> values, int defaultValue, int min, int max, params string[] names)
        {
            var raw = GetString(values, names);
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return Math.Max(min, Math.Min(max, value));
            }

            return defaultValue;
        }

        private static bool GetBool(Dictionary<string, string> values, params string[] names)
        {
            var raw = GetString(values, names);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return raw == "1" ||
                   raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("sí", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<ApiHostSnapshot> GetSnapshots()
        {
            var provider = HostSnapshotProvider;
            if (provider == null)
            {
                return new List<ApiHostSnapshot>();
            }

            try
            {
                return provider() ?? new List<ApiHostSnapshot>();
            }
            catch
            {
                return new List<ApiHostSnapshot>();
            }
        }

        private static object MapHost(ApiHostSnapshot host)
        {
            return new Dictionary<string, object>
            {
                { "hostname", host.Hostname ?? string.Empty },
                { "alias", host.Alias ?? string.Empty },
                { "status", host.Status ?? string.Empty },
                { "active", host.IsActive },
                { "sent", host.Sent },
                { "received", host.Received },
                { "lost", host.Lost },
                { "error", host.Error },
                { "lossPercent", host.Sent > 0 ? Math.Round((double)host.Lost * 100.0 / host.Sent, 2) : 0 },
                {
                    "rttMs", new Dictionary<string, object>
                    {
                        { "current", host.CurrentRttMs },
                        { "min", host.Received > 0 ? (object)host.MinRttMs : null },
                        { "avg", host.Received > 0 ? (object)Math.Round(host.AvgRttMs, 2) : null },
                        { "max", host.Received > 0 ? (object)host.MaxRttMs : null }
                    }
                }
            };
        }

        private static object BuildHealth()
        {
            var uptime = IsRunning ? (long)Math.Max(0, (DateTime.UtcNow - _startedUtc).TotalSeconds) : 0;

            return new Dictionary<string, object>
            {
                { "status", IsRunning ? "ok" : "stopped" },
                { "app", "vmPing" },
                { "version", typeof(ApiService).Assembly.GetName().Version?.ToString() ?? string.Empty },
                { "machine", Environment.MachineName },
                { "timeUtc", DateTime.UtcNow },
                { "uptimeSeconds", uptime },
                { "listen", IsListeningOnLan ? "0.0.0.0" : "127.0.0.1" },
                { "port", Port },
                { "corsEnabled", ApplicationOptions.ApiCorsEnabled },
                { "database", HistoryService.DatabasePath ?? string.Empty },
                { "historyEnabled", HistoryService.IsInitialized },
                { "hostsMonitored", GetSnapshots().Count }
            };
        }

        private static object BuildHosts()
        {
            var hosts = GetSnapshots()
                .Where(h => h != null)
                .OrderBy(h => h.Alias ?? h.Hostname, StringComparer.OrdinalIgnoreCase)
                .Select(MapHost)
                .ToList();

            return new Dictionary<string, object>
            {
                { "count", hosts.Count },
                { "hosts", hosts }
            };
        }

        private static ApiResponse BuildHostDetail(string host, Dictionary<string, string> values)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return Error(400, "Indique el host en la ruta: /api/hosts/{host}.");
            }

            var snapshot = GetSnapshots().FirstOrDefault(h =>
                string.Equals(h.Hostname, host, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(h.Alias, host, StringComparison.OrdinalIgnoreCase));

            var knownHosts = HistoryService.GetKnownHosts();
            var databaseHost = snapshot != null
                ? snapshot.Hostname
                : knownHosts.FirstOrDefault(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));

            var range = ResolveRange(values, DateTime.UtcNow.AddHours(-24), DateTime.UtcNow);
            var sampleLimit = GetInt(values, 100, 1, 5000, "samples", "limit");
            var eventLimit = GetInt(values, 50, 1, 2000, "events");

            var samples = HistoryService.QuerySamples(databaseHost, range.StartUtc, range.EndUtc, sampleLimit);
            var events = HistoryService.QueryStatusChanges(databaseHost, range.StartUtc, range.EndUtc, eventLimit);

            if (snapshot == null && databaseHost == null && samples.Count == 0 && events.Count == 0)
            {
                return Error(404, $"No hay datos para el host '{host}'.");
            }

            return Ok(new Dictionary<string, object>
            {
                { "host", snapshot != null ? MapHost(snapshot) : null },
                {
                    "range", new Dictionary<string, object>
                    {
                        { "fromUtc", range.StartUtc },
                        { "toUtc", range.EndUtc }
                    }
                },
                { "samplesCount", samples.Count },
                { "samples", MapSamples(samples) },
                { "eventsCount", events.Count },
                { "events", MapEvents(events) }
            });
        }

        private static object BuildSamples(Dictionary<string, string> values)
        {
            var host = GetString(values, "host", "equipo", "hostname", "ip");
            var range = ResolveRange(values, DateTime.UtcNow.AddHours(-24), DateTime.UtcNow);
            var limit = GetInt(values, 500, 1, 5000, "limit", "n");

            var samples = HistoryService.QuerySamples(host, range.StartUtc, range.EndUtc, limit);

            return new Dictionary<string, object>
            {
                { "count", samples.Count },
                {
                    "range", new Dictionary<string, object>
                    {
                        { "fromUtc", range.StartUtc },
                        { "toUtc", range.EndUtc }
                    }
                },
                { "samples", MapSamples(samples) }
            };
        }

        private static object BuildEvents(Dictionary<string, string> values)
        {
            var host = GetString(values, "host", "equipo", "hostname", "ip");
            var range = ResolveRange(values, DateTime.UtcNow.AddHours(-24), DateTime.UtcNow);
            var limit = GetInt(values, 200, 1, 2000, "limit", "n");

            var events = HistoryService.QueryStatusChanges(host, range.StartUtc, range.EndUtc, limit);

            return new Dictionary<string, object>
            {
                { "count", events.Count },
                {
                    "range", new Dictionary<string, object>
                    {
                        { "fromUtc", range.StartUtc },
                        { "toUtc", range.EndUtc }
                    }
                },
                { "events", MapEvents(events) }
            };
        }

        private static object BuildStats(Dictionary<string, string> values)
        {
            var host = GetString(values, "host", "equipo", "hostname", "ip");
            var range = ResolveRange(values, DateTime.UtcNow.AddHours(-24), DateTime.UtcNow);

            var aggregates = HistoryService.GetHostAggregates(host, range.StartUtc, range.EndUtc);

            return new Dictionary<string, object>
            {
                {
                    "range", new Dictionary<string, object>
                    {
                        { "fromUtc", range.StartUtc },
                        { "toUtc", range.EndUtc }
                    }
                },
                { "hosts", aggregates.Select(a => new Dictionary<string, object>
                    {
                        { "hostname", a.Hostname },
                        { "alias", a.Alias ?? string.Empty },
                        { "samples", a.Samples },
                        { "ok", a.Ok },
                        { "failed", a.Failed },
                        { "availabilityPercent", Math.Round(a.AvailabilityPercent, 2) },
                        { "rttMinMs", a.MinRttMs },
                        { "rttAvgMs", a.AvgRttMs.HasValue ? (object)Math.Round(a.AvgRttMs.Value, 2) : null },
                        { "rttMaxMs", a.MaxRttMs }
                    }).ToList()
                }
            };
        }

        private static ApiResponse BuildDaily(Dictionary<string, string> values)
        {
            var host = GetString(values, "host", "equipo", "hostname", "ip");
            if (string.IsNullOrWhiteSpace(host))
            {
                return Error(400, "Indique el host: /api/daily?host=10.1.1.50&date=2026-11-16");
            }

            DateTime localDay;
            var date = GetString(values, "date", "dia", "day");
            if (!string.IsNullOrWhiteSpace(date))
            {
                if (!DateTime.TryParseExact(
                    date,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out localDay))
                {
                    return Error(400, "Formato de fecha inválido. Use yyyy-MM-dd.");
                }
            }
            else
            {
                var range = ResolveRange(values, DateTime.Now.Date, DateTime.Now);
                localDay = range.StartUtc.ToLocalTime().Date;
            }

            var dayStartLocal = DateTime.SpecifyKind(localDay.Date, DateTimeKind.Local);
            var dayEndLocal = dayStartLocal.AddDays(1);
            var samples = HistoryService.QuerySamples(
                host,
                dayStartLocal.ToUniversalTime(),
                dayEndLocal.ToUniversalTime(),
                200000);

            var buckets = new List<object>();
            for (var hour = 0; hour < 24; hour++)
            {
                var hourSamples = samples
                    .Where(s => s.TimestampUtc.ToLocalTime().Hour == hour)
                    .ToList();
                var ok = hourSamples.Count(s => s.Success);
                var failures = hourSamples.Count - ok;
                var rtts = hourSamples
                    .Where(s => s.Success && s.LatencyMs.HasValue)
                    .Select(s => s.LatencyMs.Value)
                    .ToList();

                buckets.Add(new Dictionary<string, object>
                {
                    { "hour", hour },
                    { "samples", hourSamples.Count },
                    { "ok", ok },
                    { "failed", failures },
                    {
                        "availabilityPercent",
                        hourSamples.Count > 0 ? (object)Math.Round(ok * 100.0 / hourSamples.Count, 2) : null
                    },
                    {
                        "rttAvgMs",
                        rtts.Count > 0 ? (object)Math.Round(rtts.Average(), 2) : null
                    }
                });
            }

            return Ok(new Dictionary<string, object>
            {
                { "host", host },
                { "date", localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "samples", samples.Count },
                { "hours", buckets }
            });
        }

        private static object BuildInventory(Dictionary<string, string> values)
        {
            var host = GetString(values, "host", "equipo", "hostname", "ip");
            var devices = InventoryStore.Load();

            if (!string.IsNullOrWhiteSpace(host))
            {
                devices = devices.Where(d =>
                    string.Equals(d.Host, host, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.DnsHostname, host, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Alias, host, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var mapped = devices.Select(d => new Dictionary<string, object>
            {
                { "host", d.Host ?? string.Empty },
                { "alias", d.Alias ?? string.Empty },
                { "reachable", d.IsReachable },
                { "source", d.Source ?? string.Empty },
                { "collectedUtc", d.CollectedUtc },
                { "serialNumber", d.SerialNumber ?? string.Empty },
                { "manufacturer", d.Manufacturer ?? string.Empty },
                { "model", d.Model ?? string.Empty },
                { "os", d.OsCaption ?? string.Empty },
                { "osVersion", d.OsVersion ?? string.Empty },
                { "cpu", d.CpuName ?? string.Empty },
                { "ramGB", d.TotalRamGB },
                { "ipv4", d.Ipv4 ?? string.Empty },
                { "mac", d.MacAddresses ?? string.Empty },
                { "domain", d.Domain ?? string.Empty },
                { "snmpLocation", d.SnmpLocation ?? string.Empty },
                { "snmpContact", d.SnmpContact ?? string.Empty },
                { "snmpUptime", d.SnmpUptime ?? string.Empty }
            }).ToList();

            return new Dictionary<string, object>
            {
                { "count", mapped.Count },
                { "devices", mapped }
            };
        }

        private static ApiResponse BuildPing(Dictionary<string, string> values)
        {
            var target = GetString(values, "host", "target", "ip", "equipo");
            if (string.IsNullOrWhiteSpace(target))
            {
                return Error(400, "Indique el host: /api/ping?host=10.1.1.50");
            }

            var count = GetInt(values, 1, 1, 20, "count", "n");
            var timeout = GetInt(values, 2000, 200, 10000, "timeout", "timeoutMs");
            var requestedType = GetString(values, "type", "tipo");
            var save = GetBool(values, "save", "guardar");

            string probeHost = target;
            int tcpPort = 0;
            var tcp = string.Equals(requestedType, "tcp", StringComparison.OrdinalIgnoreCase);

            if (!tcp && TryParseTcpTarget(target, out var parsedHost, out var parsedPort))
            {
                probeHost = parsedHost;
                tcpPort = parsedPort;
                tcp = true;
            }
            else if (tcp && TryParseTcpTarget(target, out var explicitHost, out var explicitPort))
            {
                probeHost = explicitHost;
                tcpPort = explicitPort;
            }

            if (tcp && (tcpPort < 1 || tcpPort > 65535))
            {
                return Error(400, "Para TCP indique el puerto en el host, por ejemplo: host=10.1.1.50:443");
            }

            string resolved = string.Empty;
            try
            {
                var addresses = Dns.GetHostAddresses(probeHost);
                if (addresses.Length > 0)
                {
                    resolved = addresses[0].ToString();
                }
            }
            catch
            {
                // La resolución fallará igualmente en el intento de ping.
            }

            var attempts = new List<Dictionary<string, object>>();
            var successCount = 0;
            var rttValues = new List<long>();
            var firstError = string.Empty;
            var probeType = tcp ? "API-TCP" : "API-ICMP";

            if (tcp)
            {
                for (var i = 1; i <= count; i++)
                {
                    var success = false;
                    long? latency = null;
                    var error = string.Empty;
                    var watch = Stopwatch.StartNew();

                    try
                    {
                        using (var client = new TcpClient())
                        {
                            var asyncResult = client.BeginConnect(probeHost, tcpPort, null, null);
                            if (asyncResult.AsyncWaitHandle.WaitOne(timeout))
                            {
                                client.EndConnect(asyncResult);
                                success = true;
                            }
                            else
                            {
                                error = "Tiempo de espera agotado.";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex.InnerException?.Message ?? ex.Message;
                    }

                    watch.Stop();
                    if (success)
                    {
                        latency = Math.Max(0, watch.ElapsedMilliseconds);
                    }
                    else if (string.IsNullOrWhiteSpace(firstError))
                    {
                        firstError = error;
                    }

                    attempts.Add(BuildAttempt(i, success, latency, error, probeType));
                    if (success)
                    {
                        successCount++;
                        rttValues.Add(latency ?? 0);
                    }

                    if (save)
                    {
                        HistoryService.RecordSample(
                            probeHost,
                            string.Empty,
                            probeType,
                            DateTime.UtcNow,
                            success,
                            latency,
                            error);
                    }
                }
            }
            else
            {
                using (var ping = new Ping())
                {
                    for (var i = 1; i <= count; i++)
                    {
                        var success = false;
                        long? latency = null;
                        var error = string.Empty;

                        try
                        {
                            var reply = ping.Send(probeHost, timeout, new byte[32]);
                            if (reply != null && reply.Status == IPStatus.Success)
                            {
                                success = true;
                                latency = Math.Max(0, reply.RoundtripTime);
                            }
                            else if (reply != null)
                            {
                                error = reply.Status.ToString();
                            }
                            else
                            {
                                error = "Sin respuesta.";
                            }
                        }
                        catch (Exception ex)
                        {
                            error = ex.InnerException?.Message ?? ex.Message;
                        }

                        if (success)
                        {
                            if (string.IsNullOrWhiteSpace(resolved))
                            {
                                resolved = probeHost;
                            }
                        }
                        else if (string.IsNullOrWhiteSpace(firstError))
                        {
                            firstError = error;
                        }

                        attempts.Add(BuildAttempt(i, success, latency, error, probeType));
                        if (success)
                        {
                            successCount++;
                            rttValues.Add(latency ?? 0);
                        }

                        if (save)
                        {
                            HistoryService.RecordSample(
                                probeHost,
                                string.Empty,
                                probeType,
                                DateTime.UtcNow,
                                success,
                                latency,
                                error);
                        }
                    }
                }
            }

            return Ok(new Dictionary<string, object>
            {
                { "target", target },
                { "host", probeHost },
                { "port", tcp ? (object)tcpPort : null },
                { "type", tcp ? "tcp" : "icmp" },
                { "resolved", resolved },
                { "reachable", successCount > 0 },
                { "count", count },
                { "sent", count },
                { "received", successCount },
                { "lost", count - successCount },
                { "lossPercent", Math.Round((count - successCount) * 100.0 / count, 2) },
                { "rttMinMs", rttValues.Count > 0 ? (object)rttValues.Min() : null },
                { "rttAvgMs", rttValues.Count > 0 ? (object)Math.Round(rttValues.Average(), 2) : null },
                { "rttMaxMs", rttValues.Count > 0 ? (object)rttValues.Max() : null },
                { "error", firstError },
                { "attempts", attempts },
                { "timestampUtc", DateTime.UtcNow }
            });
        }

        private static Dictionary<string, object> BuildAttempt(
            int sequence,
            bool success,
            long? latencyMs,
            string error,
            string probeType)
        {
            return new Dictionary<string, object>
            {
                { "seq", sequence },
                { "success", success },
                { "rttMs", latencyMs },
                { "status", success ? "Success" : "Failed" },
                { "error", error ?? string.Empty },
                { "probeType", probeType },
                { "timestampUtc", DateTime.UtcNow }
            };
        }

        private static bool TryParseTcpTarget(string target, out string host, out int port)
        {
            host = target;
            port = 0;

            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (target.StartsWith("["))
            {
                var closing = target.IndexOf(']');
                if (closing > 0 &&
                    closing + 2 < target.Length &&
                    target[closing + 1] == ':')
                {
                    var portText = target.Substring(closing + 2);
                    if (int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) &&
                        port >= 1 && port <= 65535)
                    {
                        host = target.Substring(1, closing - 1);
                        return true;
                    }
                }

                return false;
            }

            var firstColon = target.IndexOf(':');
            var lastColon = target.LastIndexOf(':');
            if (firstColon > 0 && firstColon == lastColon)
            {
                var portText = target.Substring(lastColon + 1);
                if (int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) &&
                    port >= 1 && port <= 65535)
                {
                    host = target.Substring(0, lastColon);
                    return true;
                }
            }

            return false;
        }

        private static List<object> MapSamples(IEnumerable<ProbeSampleRow> samples)
        {
            return samples.Select(s => (object)new Dictionary<string, object>
            {
                { "hostname", s.Hostname },
                { "alias", s.Alias ?? string.Empty },
                { "probeType", s.ProbeType ?? string.Empty },
                { "timestampUtc", s.TimestampUtc },
                { "timestampLocal", s.TimestampUtc.ToLocalTime() },
                { "success", s.Success },
                { "latencyMs", s.LatencyMs },
                { "error", s.Error ?? string.Empty }
            }).ToList();
        }

        private static List<object> MapEvents(IEnumerable<StatusChangeRow> events)
        {
            return events.Select(e => (object)new Dictionary<string, object>
            {
                { "hostname", e.Hostname },
                { "alias", e.Alias ?? string.Empty },
                { "oldStatus", e.OldStatus ?? string.Empty },
                { "newStatus", e.NewStatus ?? string.Empty },
                { "timestampUtc", e.TimestampUtc },
                { "timestampLocal", e.TimestampUtc.ToLocalTime() },
                { "reason", e.Reason ?? string.Empty }
            }).ToList();
        }

        private sealed class ResolvedRange
        {
            public DateTime StartUtc { get; set; }
            public DateTime EndUtc { get; set; }
        }

        private static ResolvedRange ResolveRange(
            Dictionary<string, string> values,
            DateTime defaultStartUtc,
            DateTime defaultEndUtc)
        {
            var startUtc = defaultStartUtc;
            var endUtc = defaultEndUtc;

            var from = GetString(values, "from", "desde", "start");
            var to = GetString(values, "to", "hasta", "end");

            if (!string.IsNullOrWhiteSpace(from))
            {
                startUtc = ParseDateToken(from, false) ?? startUtc;
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                endUtc = ParseDateToken(to, true) ?? endUtc;
            }

            if (startUtc > endUtc)
            {
                var swap = startUtc;
                startUtc = endUtc;
                endUtc = swap;
            }

            return new ResolvedRange { StartUtc = startUtc, EndUtc = endUtc };
        }

        private static DateTime? ParseDateToken(string token, bool isEnd)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            token = token.Trim();

            if (token.Equals("now", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("ahora", StringComparison.OrdinalIgnoreCase))
            {
                return DateTime.UtcNow;
            }

            if (token.Equals("today", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("hoy", StringComparison.OrdinalIgnoreCase))
            {
                return isEnd ? DateTime.Today.AddDays(1).AddMilliseconds(-1) : DateTime.Today;
            }

            if (token.Equals("yesterday", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("ayer", StringComparison.OrdinalIgnoreCase))
            {
                return isEnd ? DateTime.Today.AddMilliseconds(-1) : DateTime.Today.AddDays(-1);
            }

            if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 2)
            {
                var numberText = token.Substring(1, token.Length - 2);
                var unit = char.ToLowerInvariant(token[token.Length - 1]);
                if (double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
                {
                    switch (unit)
                    {
                        case 'm':
                            return DateTime.UtcNow.AddMinutes(-amount);
                        case 'h':
                            return DateTime.UtcNow.AddHours(-amount);
                        case 'd':
                            return DateTime.UtcNow.AddDays(-amount);
                        case 'w':
                            return DateTime.UtcNow.AddDays(-amount * 7);
                    }
                }
            }

            if (DateTime.TryParseExact(
                token,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateOnly))
            {
                var local = DateTime.SpecifyKind(dateOnly.Date, DateTimeKind.Local);
                return isEnd ? local.AddDays(1).AddMilliseconds(-1).ToUniversalTime() : local.ToUniversalTime();
            }

            if (DateTimeOffset.TryParse(
                token,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var offset))
            {
                return offset.ToUniversalTime().UtcDateTime;
            }

            if (DateTime.TryParse(
                token,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            return null;
        }

        private static object BuildApiIndex()
        {
            return new Dictionary<string, object>
            {
                { "name", "vmPing API" },
                { "version", typeof(ApiService).Assembly.GetName().Version?.ToString() ?? string.Empty },
                {
                    "endpoints", new List<object>
                    {
                        Endpoint("GET", "/api/health", "Estado de la API, versión, base de datos y hosts monitoreados."),
                        Endpoint("GET", "/api/hosts", "Hosts monitoreados en vivo con estado y latencia."),
                        Endpoint("GET", "/api/hosts/{host}", "Detalle en vivo + historial de un host. Parámetros: from, to, samples, events."),
                        Endpoint("GET", "/api/ping", "Ping ICMP/TCP bajo demanda. Parámetros: host, count, timeout, type, save."),
                        Endpoint("GET", "/api/samples", "Muestras históricas. Parámetros: host, from, to, limit."),
                        Endpoint("GET", "/api/events", "Cambios de estado. Parámetros: host, from, to, limit."),
                        Endpoint("GET", "/api/stats", "Disponibilidad agregada. Parámetros: host, from, to."),
                        Endpoint("GET", "/api/daily", "Resumen por hora de un día. Parámetros: host, date=YYYY-MM-DD."),
                        Endpoint("GET", "/api/inventory", "Inventario de equipos. Parámetros: host."),
                        Endpoint("GET", "/api/openapi.json", "Especificación OpenAPI 3.0.")
                    }
                },
                {
                    "dates", new Dictionary<string, object>
                    {
                        { "format", "ISO-8601 o yyyy-MM-dd" },
                        { "examples", new[] { "2026-11-16", "2026-11-16T08:00:00Z", "-7d", "-24h", "today", "yesterday" } }
                    }
                }
            };
        }

        private static object Endpoint(string method, string path, string description)
        {
            return new Dictionary<string, object>
            {
                { "method", method },
                { "path", path },
                { "description", description }
            };
        }

        private static string BuildLandingPage()
        {
            var baseUrl = LocalUrl;
            return @"<!DOCTYPE html>
<html lang=""es"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>vmPing API</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f3f4f6;color:#111827}
header{background:#1d4ed8;color:#fff;padding:24px 32px}
main{max-width:960px;margin:24px auto;padding:0 16px}
.card{background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:20px;margin-bottom:16px;box-shadow:0 1px 2px rgba(0,0,0,.05)}
code{background:#f3f4f6;padding:2px 6px;border-radius:4px}
a{color:#1d4ed8}
table{width:100%;border-collapse:collapse}
td,th{padding:6px 8px;border-bottom:1px solid #e5e7eb;text-align:left;font-size:14px}
input,button{padding:8px;border-radius:6px;border:1px solid #d1d5db;font-size:14px}
button{background:#1d4ed8;color:#fff;border:0;cursor:pointer}
pre{background:#111827;color:#e5e7eb;padding:12px;border-radius:8px;overflow:auto}
</style>
</head>
<body>
<header><h1 style=""margin:0"">vmPing API</h1><div>Datos de monitoreo para bitácoras e integraciones</div></header>
<main>
<div class=""card"">
<h2>Prueba rápida</h2>
<p>Host o IP: <input id=""host"" value=""127.0.0.1"" size=""28""> <button onclick=""probar()"">Ping</button></p>
<pre id=""out"">Esperando prueba...</pre>
</div>
<div class=""card"">
<h2>Endpoints</h2>
<table>
<tr><th>Método</th><th>Ruta</th><th>Descripción</th></tr>
<tr><td>GET</td><td><a href=""/api/health"">/api/health</a></td><td>Estado de la API y base de datos</td></tr>
<tr><td>GET</td><td><a href=""/api/hosts"">/api/hosts</a></td><td>Hosts monitoreados en vivo</td></tr>
<tr><td>GET</td><td><a href=""/api/hosts/127.0.0.1"">/api/hosts/{host}</a></td><td>Detalle + historial</td></tr>
<tr><td>GET</td><td>/api/ping?host=10.1.1.50</td><td>Ping bajo demanda</td></tr>
<tr><td>GET</td><td>/api/samples?host=10.1.1.50&amp;from=-7d</td><td>Muestras históricas</td></tr>
<tr><td>GET</td><td>/api/events?from=-7d</td><td>Cambios de estado</td></tr>
<tr><td>GET</td><td>/api/stats?from=today</td><td>Disponibilidad agregada</td></tr>
<tr><td>GET</td><td>/api/daily?host=10.1.1.50&amp;date=2026-11-16</td><td>Resumen por hora</td></tr>
<tr><td>GET</td><td>/api/inventory</td><td>Inventario de equipos</td></tr>
<tr><td>GET</td><td><a href=""/api/openapi.json"">/api/openapi.json</a></td><td>Especificación OpenAPI</td></tr>
</table>
</div>
<div class=""card"">
<h2>Ejemplo</h2>
<pre>curl ""{BASE}/api/ping?host=10.1.1.50&amp;count=2""</pre>
</div>
</main>
<script>
const BASE = location.origin;
async function probar(){
  const host = document.getElementById('host').value.trim() || '127.0.0.1';
  const out = document.getElementById('out');
  out.textContent = 'Consultando...';
  try {
    const r = await fetch(BASE + '/api/ping?host=' + encodeURIComponent(host) + '&amp;count=1');
    out.textContent = JSON.stringify(await r.json(), null, 2);
  } catch (e) {
    out.textContent = 'Error: ' + e;
  }
}
</script>
</body>
</html>".Replace("{BASE}", baseUrl);
        }

        private const string OpenApiSpec = @"{
  ""openapi"": ""3.0.3"",
  ""info"": {
    ""title"": ""vmPing API"",
    ""description"": ""API local de vmPing para consultar hosts, historial SQLite, inventario y ejecutar pings bajo demanda."",
    ""version"": ""1.0.0""
  },
  ""servers"": [ { ""url"": ""http://127.0.0.1:8099"" } ],
  ""security"": [ { ""ApiKey"": [] } ],
  ""components"": {
    ""securitySchemes"": {
      ""ApiKey"": { ""type"": ""apiKey"", ""in"": ""header"", ""name"": ""X-Api-Key"" }
    }
  },
  ""paths"": {
    ""/api/health"": { ""get"": { ""summary"": ""Estado de la API"" } },
    ""/api/hosts"": { ""get"": { ""summary"": ""Hosts monitoreados en vivo"" } },
    ""/api/hosts/{host}"": {
      ""get"": {
        ""summary"": ""Detalle de un host"",
        ""parameters"": [
          { ""name"": ""host"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } },
          { ""name"": ""from"", ""in"": ""query"", ""schema"": { ""type"": ""string"" } },
          { ""name"": ""to"", ""in"": ""query"", ""schema"": { ""type"": ""string"" } },
          { ""name"": ""samples"", ""in"": ""query"", ""schema"": { ""type"": ""integer"" } },
          { ""name"": ""events"", ""in"": ""query"", ""schema"": { ""type"": ""integer"" } }
        ]
      }
    },
    ""/api/ping"": {
      ""get"": {
        ""summary"": ""Ping ICMP o TCP bajo demanda"",
        ""parameters"": [
          { ""name"": ""host"", ""in"": ""query"", ""required"": true, ""schema"": { ""type"": ""string"" } },
          { ""name"": ""count"", ""in"": ""query"", ""schema"": { ""type"": ""integer"", ""default"": 1 } },
          { ""name"": ""timeout"", ""in"": ""query"", ""schema"": { ""type"": ""integer"", ""default"": 2000 } },
          { ""name"": ""type"", ""in"": ""query"", ""schema"": { ""type"": ""string"", ""enum"": [ ""icmp"", ""tcp"" ] } },
          { ""name"": ""save"", ""in"": ""query"", ""schema"": { ""type"": ""boolean"", ""default"": false } }
        ]
      }
    },
    ""/api/samples"": { ""get"": { ""summary"": ""Muestras históricas"" } },
    ""/api/events"": { ""get"": { ""summary"": ""Cambios de estado"" } },
    ""/api/stats"": { ""get"": { ""summary"": ""Disponibilidad agregada"" } },
    ""/api/daily"": { ""get"": { ""summary"": ""Resumen por hora"" } },
    ""/api/inventory"": { ""get"": { ""summary"": ""Inventario de equipos"" } }
  }
}";
    }
}
