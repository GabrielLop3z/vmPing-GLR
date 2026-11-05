using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace vmPing.Classes
{
    public class ProbeSampleRow
    {
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public string ProbeType { get; set; }
        public DateTime TimestampUtc { get; set; }
        public bool Success { get; set; }
        public long? LatencyMs { get; set; }
        public string Error { get; set; }
    }

    public class StatusChangeRow
    {
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public string OldStatus { get; set; }
        public string NewStatus { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Reason { get; set; }
    }

    public static class HistoryService
    {
        private const string TimestampFormat = "o";

        private static readonly object InitLock = new object();
        private static readonly BlockingCollection<Action<SQLiteConnection>> Queue =
            new BlockingCollection<Action<SQLiteConnection>>();

        private static readonly ConcurrentDictionary<string, SampleState> SampleStates =
            new ConcurrentDictionary<string, SampleState>(StringComparer.OrdinalIgnoreCase);

        private static string _connectionString;
        private static string _databasePath;
        private static Thread _worker;
        private static bool _initialized;

        public static string DatabasePath => _databasePath;
        public static bool IsInitialized => _initialized;

        private sealed class SampleState
        {
            public DateTime LastQueuedUtc = DateTime.MinValue;
            public bool? LastSuccess;
        }

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            lock (InitLock)
            {
                if (_initialized)
                {
                    return;
                }

                var configDirectory = Path.GetDirectoryName(Configuration.FilePath);
                if (string.IsNullOrWhiteSpace(configDirectory))
                {
                    configDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "vmPing");
                }

                Directory.CreateDirectory(configDirectory);
                _databasePath = Path.Combine(configDirectory, "vmPing.db");
                _connectionString = $"Data Source={_databasePath};Version=3;";

                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    Execute(connection, "PRAGMA journal_mode=WAL;");
                    Execute(connection, "PRAGMA synchronous=NORMAL;");
                    Execute(connection, "PRAGMA busy_timeout=5000;");
                    CreateSchema(connection);
                }

                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "vmPing.HistoryWriter"
                };
                _worker.Start();
                _initialized = true;

                Task.Run(() => PruneOldData(ApplicationOptions.HistoryRetentionDays));
            }
        }

        public static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                Queue.CompleteAdding();
                _worker?.Join(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Best effort.
            }

            _initialized = false;
        }

        public static void RecordSample(
            string hostname,
            string alias,
            string probeType,
            DateTime timestampUtc,
            bool success,
            long? latencyMs,
            string error)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(hostname))
            {
                return;
            }

            var key = $"{hostname}|{probeType}";
            var state = SampleStates.GetOrAdd(key, _ => new SampleState());

            lock (state)
            {
                var interval = Math.Max(1, ApplicationOptions.HistorySampleSeconds);
                var changed = state.LastSuccess.HasValue && state.LastSuccess.Value != success;
                var due = (timestampUtc - state.LastQueuedUtc).TotalSeconds >= interval;

                if (!changed && !due && state.LastSuccess.HasValue)
                {
                    return;
                }

                state.LastQueuedUtc = timestampUtc;
                state.LastSuccess = success;
            }

            var host = hostname.Trim();
            var aliasValue = alias ?? string.Empty;
            var typeValue = probeType ?? "ICMP";
            var timestamp = Format(timestampUtc);
            var errorValue = error ?? string.Empty;

            QueueWork(connection =>
            {
                using (var hostCommand = connection.CreateCommand())
                {
                    hostCommand.CommandText = @"
INSERT INTO hosts (hostname, alias, first_seen_utc, last_seen_utc)
VALUES (@host, @alias, @ts, @ts)
ON CONFLICT(hostname) DO UPDATE SET
    alias = CASE WHEN excluded.alias <> '' THEN excluded.alias ELSE hosts.alias END,
    last_seen_utc = excluded.last_seen_utc;";
                    hostCommand.Parameters.AddWithValue("@host", host);
                    hostCommand.Parameters.AddWithValue("@alias", aliasValue);
                    hostCommand.Parameters.AddWithValue("@ts", timestamp);
                    hostCommand.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
INSERT INTO probe_samples
    (hostname, alias, probe_type, timestamp_utc, success, latency_ms, error)
VALUES
    (@host, @alias, @type, @ts, @success, @latency, @error);";
                    command.Parameters.AddWithValue("@host", host);
                    command.Parameters.AddWithValue("@alias", aliasValue);
                    command.Parameters.AddWithValue("@type", typeValue);
                    command.Parameters.AddWithValue("@ts", timestamp);
                    command.Parameters.AddWithValue("@success", success ? 1 : 0);
                    command.Parameters.AddWithValue("@latency", latencyMs.HasValue ? (object)latencyMs.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@error", errorValue);
                    command.ExecuteNonQuery();
                }
            });
        }

        public static void RecordStatusChange(
            string hostname,
            string alias,
            string oldStatus,
            string newStatus,
            DateTime timestampUtc,
            string reason)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(hostname))
            {
                return;
            }

            var host = hostname.Trim();
            var aliasValue = alias ?? string.Empty;
            var oldValue = oldStatus ?? string.Empty;
            var newValue = newStatus ?? string.Empty;
            var reasonValue = reason ?? string.Empty;
            var timestamp = Format(timestampUtc);

            QueueWork(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
INSERT INTO status_changes
    (hostname, alias, old_status, new_status, timestamp_utc, reason)
VALUES
    (@host, @alias, @old, @new, @ts, @reason);";
                    command.Parameters.AddWithValue("@host", host);
                    command.Parameters.AddWithValue("@alias", aliasValue);
                    command.Parameters.AddWithValue("@old", oldValue);
                    command.Parameters.AddWithValue("@new", newValue);
                    command.Parameters.AddWithValue("@ts", timestamp);
                    command.Parameters.AddWithValue("@reason", reasonValue);
                    command.ExecuteNonQuery();
                }
            });
        }

        public static void RecordAudit(string action, string entity, string details)
        {
            if (!_initialized)
            {
                return;
            }

            var timestamp = Format(DateTime.UtcNow);
            var user = Environment.UserName ?? string.Empty;
            var actionValue = action ?? string.Empty;
            var entityValue = entity ?? string.Empty;
            var detailsValue = details ?? string.Empty;

            QueueWork(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
INSERT INTO audit_log (timestamp_utc, user_name, action, entity, details)
VALUES (@ts, @user, @action, @entity, @details);";
                    command.Parameters.AddWithValue("@ts", timestamp);
                    command.Parameters.AddWithValue("@user", user);
                    command.Parameters.AddWithValue("@action", actionValue);
                    command.Parameters.AddWithValue("@entity", entityValue);
                    command.Parameters.AddWithValue("@details", detailsValue);
                    command.ExecuteNonQuery();
                }
            });
        }

        public static void RecordReportRun(DateTime rangeStartUtc, DateTime rangeEndUtc, string recipients, string status, string error)
        {
            if (!_initialized)
            {
                return;
            }

            var timestamp = Format(DateTime.UtcNow);
            var start = Format(rangeStartUtc);
            var end = Format(rangeEndUtc);
            var recipientValue = recipients ?? string.Empty;
            var statusValue = status ?? string.Empty;
            var errorValue = error ?? string.Empty;

            QueueWork(connection =>
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
INSERT INTO report_runs
    (timestamp_utc, range_start_utc, range_end_utc, recipients, status, error)
VALUES (@ts, @start, @end, @recipients, @status, @error);";
                    command.Parameters.AddWithValue("@ts", timestamp);
                    command.Parameters.AddWithValue("@start", start);
                    command.Parameters.AddWithValue("@end", end);
                    command.Parameters.AddWithValue("@recipients", recipientValue);
                    command.Parameters.AddWithValue("@status", statusValue);
                    command.Parameters.AddWithValue("@error", errorValue);
                    command.ExecuteNonQuery();
                }
            });
        }

        public static List<ProbeSampleRow> GetSamples(DateTime rangeStartUtc, DateTime rangeEndUtc)
        {
            var result = new List<ProbeSampleRow>();
            if (!_initialized)
            {
                return result;
            }

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT hostname, alias, probe_type, timestamp_utc, success, latency_ms, error
FROM probe_samples
WHERE timestamp_utc >= @start AND timestamp_utc <= @end
ORDER BY hostname, timestamp_utc;";
                    command.Parameters.AddWithValue("@start", Format(rangeStartUtc));
                    command.Parameters.AddWithValue("@end", Format(rangeEndUtc));

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new ProbeSampleRow
                            {
                                Hostname = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                                Alias = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                ProbeType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                TimestampUtc = Parse(reader.IsDBNull(3) ? string.Empty : reader.GetString(3)),
                                Success = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                                LatencyMs = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
                                Error = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
                            });
                        }
                    }
                }
            }

            return result;
        }

        public static List<StatusChangeRow> GetStatusChanges(DateTime rangeStartUtc, DateTime rangeEndUtc)
        {
            var result = new List<StatusChangeRow>();
            if (!_initialized)
            {
                return result;
            }

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT hostname, alias, old_status, new_status, timestamp_utc, reason
FROM status_changes
WHERE timestamp_utc >= @start AND timestamp_utc <= @end
ORDER BY hostname, timestamp_utc;";
                    command.Parameters.AddWithValue("@start", Format(rangeStartUtc));
                    command.Parameters.AddWithValue("@end", Format(rangeEndUtc));

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new StatusChangeRow
                            {
                                Hostname = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                                Alias = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                OldStatus = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                NewStatus = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                TimestampUtc = Parse(reader.IsDBNull(4) ? string.Empty : reader.GetString(4)),
                                Reason = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
                            });
                        }
                    }
                }
            }

            return result;
        }

        public static List<string> GetKnownHosts()
        {
            var result = new List<string>();
            if (!_initialized)
            {
                return result;
            }

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT hostname FROM hosts ORDER BY hostname;";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                result.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }

            return result;
        }

        public static void PruneOldData(int retentionDays)
        {
            if (!_initialized)
            {
                return;
            }

            var days = Math.Max(1, retentionDays);
            var cutoff = Format(DateTime.UtcNow.AddDays(-days));
            var auditCutoff = Format(DateTime.UtcNow.AddDays(-Math.Max(days, 365)));

            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "DELETE FROM probe_samples WHERE timestamp_utc < @cutoff;";
                        command.Parameters.AddWithValue("@cutoff", cutoff);
                        command.ExecuteNonQuery();

                        command.CommandText = "DELETE FROM status_changes WHERE timestamp_utc < @cutoff;";
                        command.ExecuteNonQuery();

                        command.CommandText = "DELETE FROM audit_log WHERE timestamp_utc < @auditCutoff;";
                        command.Parameters.AddWithValue("@auditCutoff", auditCutoff);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                // Best effort.
            }
        }

        private static void CreateSchema(SQLiteConnection connection)
        {
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS hosts (
    hostname TEXT PRIMARY KEY,
    alias TEXT,
    first_seen_utc TEXT NOT NULL,
    last_seen_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS probe_samples (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    hostname TEXT NOT NULL,
    alias TEXT,
    probe_type TEXT,
    timestamp_utc TEXT NOT NULL,
    success INTEGER NOT NULL,
    latency_ms INTEGER,
    error TEXT
);

CREATE INDEX IF NOT EXISTS idx_probe_samples_host_ts
    ON probe_samples(hostname, timestamp_utc);

CREATE INDEX IF NOT EXISTS idx_probe_samples_ts
    ON probe_samples(timestamp_utc);

CREATE TABLE IF NOT EXISTS status_changes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    hostname TEXT NOT NULL,
    alias TEXT,
    old_status TEXT,
    new_status TEXT,
    timestamp_utc TEXT NOT NULL,
    reason TEXT
);

CREATE INDEX IF NOT EXISTS idx_status_changes_host_ts
    ON status_changes(hostname, timestamp_utc);

CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc TEXT NOT NULL,
    user_name TEXT,
    action TEXT NOT NULL,
    entity TEXT,
    details TEXT
);

CREATE INDEX IF NOT EXISTS idx_audit_log_ts
    ON audit_log(timestamp_utc);

CREATE TABLE IF NOT EXISTS report_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc TEXT NOT NULL,
    range_start_utc TEXT,
    range_end_utc TEXT,
    recipients TEXT,
    status TEXT,
    error TEXT
);
");
        }

        private static void QueueWork(Action<SQLiteConnection> action)
        {
            if (!_initialized || Queue.IsAddingCompleted)
            {
                return;
            }

            try
            {
                Queue.Add(action);
            }
            catch
            {
                // Best effort.
            }
        }

        private static void WorkerLoop()
        {
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    Execute(connection, "PRAGMA journal_mode=WAL;");
                    Execute(connection, "PRAGMA synchronous=NORMAL;");
                    Execute(connection, "PRAGMA busy_timeout=5000;");

                    var batch = new List<Action<SQLiteConnection>>();
                    while (!Queue.IsCompleted)
                    {
                        Action<SQLiteConnection> first;
                        if (!Queue.TryTake(out first, 500))
                        {
                            continue;
                        }

                        batch.Add(first);
                        Action<SQLiteConnection> next;
                        while (batch.Count < 250 && Queue.TryTake(out next, 0))
                        {
                            batch.Add(next);
                        }

                        using (var transaction = connection.BeginTransaction())
                        {
                            foreach (var action in batch)
                            {
                                try
                                {
                                    action(connection);
                                }
                                catch
                                {
                                    // Keep the writer alive; discard the failing item.
                                }
                            }

                            transaction.Commit();
                        }

                        batch.Clear();
                    }

                    foreach (var action in Queue.GetConsumingEnumerable())
                    {
                        try
                        {
                            action(connection);
                        }
                        catch
                        {
                            // Best effort.
                        }
                    }
                }
            }
            catch
            {
                // Never crash the app because of history persistence.
            }
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static string Format(DateTime value)
        {
            return value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        private static DateTime Parse(string value)
        {
            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            return DateTime.MinValue;
        }
    }
}
