using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace vmPing.Classes
{
    public class ReportRequest
    {
        public DateTime StartLocal { get; set; } = DateTime.Today.AddHours(7);
        public DateTime EndLocal { get; set; } = DateTime.Today.AddHours(16);
        public bool OnlyWorkHours { get; set; } = true;
        public int StartHour { get; set; } = 7;
        public int EndHour { get; set; } = 16;
        public string Recipients { get; set; } = string.Empty;
        public bool SendEmail { get; set; }
        public string OutputDirectory { get; set; }
    }

    public class ReportFailureWindow
    {
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public int FailedSamples { get; set; }
        public string LastError { get; set; }
    }

    public class HostReport
    {
        public string Hostname { get; set; }
        public string Alias { get; set; }
        public string ProbeType { get; set; }
        public int TotalSamples { get; set; }
        public int SuccessfulSamples { get; set; }
        public int FailedSamples { get; set; }
        public double AvailabilityPercent { get; set; }
        public double AverageLatencyMs { get; set; }
        public long MaxLatencyMs { get; set; }
        public int FailureWindowCount { get; set; }
        public int StatusFlaps { get; set; }
        public bool IsOk { get; set; }
        public List<ReportFailureWindow> FailureWindows { get; } = new List<ReportFailureWindow>();
    }

    public class ErrorCountRow
    {
        public string Error { get; set; }
        public int Count { get; set; }
        public int AffectedHosts { get; set; }
    }

    public class HourlyReportRow
    {
        public DateTime LocalHour { get; set; }
        public int TotalSamples { get; set; }
        public int FailedSamples { get; set; }
        public int AffectedHosts { get; set; }
        public double AvailabilityPercent { get; set; }
    }

    public class ReportSummary
    {
        public DateTime RangeStartLocal { get; set; }
        public DateTime RangeEndLocal { get; set; }
        public DateTime GeneratedLocal { get; set; }
        public int HostCount { get; set; }
        public int OkHostCount { get; set; }
        public int ProblemHostCount { get; set; }
        public int TotalSamples { get; set; }
        public int FailedSamples { get; set; }
        public double OverallAvailabilityPercent { get; set; }
        public List<HostReport> Hosts { get; } = new List<HostReport>();
        public List<ErrorCountRow> Errors { get; } = new List<ErrorCountRow>();
        public List<HourlyReportRow> Hourly { get; } = new List<HourlyReportRow>();
    }

    public class ReportResult
    {
        public bool Success { get; set; }
        public string PdfPath { get; set; }
        public string Error { get; set; }
        public ReportSummary Summary { get; set; }
    }

    public static class ReportService
    {
        private const double OkAvailabilityThreshold = 99.0;
        private const int MaxFailureRows = 300;

        public static ReportResult Generate(ReportRequest request)
        {
            var result = new ReportResult();
            try
            {
                request = request ?? new ReportRequest();
                var summary = BuildSummary(request);
                result.Summary = summary;

                var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
                    ? Path.Combine(Path.GetDirectoryName(Configuration.FilePath) ?? ".", "reports")
                    : request.OutputDirectory;

                Directory.CreateDirectory(outputDirectory);
                var fileName = $"vmPing-report-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";
                var pdfPath = Path.Combine(outputDirectory, fileName);

                RenderPdf(summary, pdfPath);
                result.PdfPath = pdfPath;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        public static ReportResult GenerateAndSend(ReportRequest request)
        {
            var result = Generate(request);
            if (!result.Success)
            {
                RecordRun(request, null, "error", result.Error);
                return result;
            }

            if (!request.SendEmail || string.IsNullOrWhiteSpace(request.Recipients))
            {
                RecordRun(request, result.PdfPath, "generated", string.Empty);
                return result;
            }

            try
            {
                SendEmail(request, result);
                RecordRun(request, result.PdfPath, "sent", string.Empty);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                RecordRun(request, result.PdfPath, "email-error", ex.Message);
            }

            return result;
        }

        public static ReportSummary BuildSummary(ReportRequest request)
        {
            var summary = new ReportSummary
            {
                RangeStartLocal = request.StartLocal,
                RangeEndLocal = request.EndLocal,
                GeneratedLocal = DateTime.Now
            };

            if (request.EndLocal <= request.StartLocal)
            {
                throw new InvalidOperationException("El rango del reporte no es valido.");
            }

            if ((request.EndLocal - request.StartLocal).TotalDays > 31)
            {
                throw new InvalidOperationException("El periodo maximo por reporte es de 31 dias.");
            }

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(request.StartLocal, DateTimeKind.Local));
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(request.EndLocal, DateTimeKind.Local));

            var samples = HistoryService.GetSamples(startUtc, endUtc)
                .Where(s => !request.OnlyWorkHours || IsInWorkHours(s.TimestampUtc, request.StartHour, request.EndHour))
                .ToList();

            var statusChanges = HistoryService.GetStatusChanges(startUtc, endUtc)
                .Where(s => !request.OnlyWorkHours || IsInWorkHours(s.TimestampUtc, request.StartHour, request.EndHour))
                .ToList();

            summary.TotalSamples = samples.Count;
            summary.FailedSamples = samples.Count(s => !s.Success);

            var flapCounts = statusChanges
                .Where(s => !string.Equals(s.Reason, "initial", StringComparison.OrdinalIgnoreCase))
                .Where(s => !string.Equals(s.NewStatus, "Stop", StringComparison.OrdinalIgnoreCase))
                .Where(s => !string.Equals(s.NewStatus, "Inactive", StringComparison.OrdinalIgnoreCase))
                .GroupBy(s => s.Hostname, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var hostGroups = samples
                .GroupBy(
                    s => new HostProbeKey
                    {
                        Host = s.Hostname,
                        Type = string.IsNullOrWhiteSpace(s.ProbeType) ? "ICMP" : s.ProbeType
                    },
                    new HostProbeKeyComparer())
                .OrderBy(g => g.Key.Host, StringComparer.OrdinalIgnoreCase);

            foreach (var group in hostGroups)
            {
                var ordered = group.OrderBy(s => s.TimestampUtc).ToList();
                var report = new HostReport
                {
                    Hostname = group.Key.Host,
                    Alias = ordered.LastOrDefault(s => !string.IsNullOrWhiteSpace(s.Alias))?.Alias ?? string.Empty,
                    ProbeType = group.Key.Type,
                    TotalSamples = ordered.Count,
                    SuccessfulSamples = ordered.Count(s => s.Success),
                    FailedSamples = ordered.Count(s => !s.Success)
                };

                report.AvailabilityPercent = report.TotalSamples == 0
                    ? 0
                    : Math.Round(100.0 * report.SuccessfulSamples / report.TotalSamples, 2);

                var latencies = ordered
                    .Where(s => s.Success && s.LatencyMs.HasValue && s.LatencyMs.Value >= 0)
                    .Select(s => s.LatencyMs.Value)
                    .ToList();

                report.AverageLatencyMs = latencies.Count == 0
                    ? 0
                    : Math.Round(latencies.Average(), 1);
                report.MaxLatencyMs = latencies.Count == 0 ? 0 : latencies.Max();

                BuildFailureWindows(ordered, report);
                report.StatusFlaps = flapCounts.TryGetValue(group.Key.Host, out var flaps) ? flaps : 0;
                report.IsOk = report.TotalSamples > 0
                    && report.AvailabilityPercent >= OkAvailabilityThreshold
                    && report.StatusFlaps < 6;

                summary.Hosts.Add(report);
            }

            summary.HostCount = summary.Hosts.Count;
            summary.OkHostCount = summary.Hosts.Count(h => h.IsOk);
            summary.ProblemHostCount = summary.HostCount - summary.OkHostCount;
            summary.OverallAvailabilityPercent = summary.TotalSamples == 0
                ? 0
                : Math.Round(100.0 * (summary.TotalSamples - summary.FailedSamples) / summary.TotalSamples, 2);

            summary.Errors.AddRange(samples
                .Where(s => !s.Success)
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Error) ? "Sin detalle" : s.Error.Trim())
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .Select(g => new ErrorCountRow
                {
                    Error = g.Key,
                    Count = g.Count(),
                    AffectedHosts = g.Select(s => s.Hostname)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count()
                }));

            summary.Hourly.AddRange(samples
                .GroupBy(s => new DateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(s.TimestampUtc, TimeZoneInfo.Local).Year,
                    TimeZoneInfo.ConvertTimeFromUtc(s.TimestampUtc, TimeZoneInfo.Local).Month,
                    TimeZoneInfo.ConvertTimeFromUtc(s.TimestampUtc, TimeZoneInfo.Local).Day,
                    TimeZoneInfo.ConvertTimeFromUtc(s.TimestampUtc, TimeZoneInfo.Local).Hour,
                    0,
                    0))
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var total = g.Count();
                    var failed = g.Count(s => !s.Success);
                    return new HourlyReportRow
                    {
                        LocalHour = g.Key,
                        TotalSamples = total,
                        FailedSamples = failed,
                        AffectedHosts = g.Where(s => !s.Success)
                            .Select(s => s.Hostname)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count(),
                        AvailabilityPercent = total == 0 ? 0 : Math.Round(100.0 * (total - failed) / total, 2)
                    };
                }));

            return summary;
        }

        private sealed class HostProbeKey
        {
            public string Host { get; set; }
            public string Type { get; set; }
        }

        private sealed class HostProbeKeyComparer : IEqualityComparer<HostProbeKey>
        {
            public bool Equals(HostProbeKey x, HostProbeKey y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                return string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Type, y.Type, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(HostProbeKey obj)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Host ?? string.Empty)
                    ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Type ?? string.Empty);
            }
        }

        private static bool IsInWorkHours(DateTime timestampUtc, int startHour, int endHour)
        {
            var localHour = TimeZoneInfo.ConvertTimeFromUtc(timestampUtc, TimeZoneInfo.Local).Hour;
            if (startHour <= endHour)
            {
                return localHour >= startHour && localHour <= endHour;
            }

            return localHour >= startHour || localHour <= endHour;
        }

        private static void BuildFailureWindows(List<ProbeSampleRow> ordered, HostReport report)
        {
            ReportFailureWindow current = null;

            foreach (var sample in ordered)
            {
                if (!sample.Success)
                {
                    if (current == null)
                    {
                        current = new ReportFailureWindow { StartUtc = sample.TimestampUtc };
                    }

                    current.EndUtc = sample.TimestampUtc;
                    current.FailedSamples++;
                    if (!string.IsNullOrWhiteSpace(sample.Error))
                    {
                        current.LastError = sample.Error;
                    }
                }
                else if (current != null)
                {
                    report.FailureWindows.Add(current);
                    current = null;
                }
            }

            if (current != null)
            {
                report.FailureWindows.Add(current);
            }

            report.FailureWindowCount = report.FailureWindows.Count;
        }

        private static void RenderPdf(ReportSummary summary, string pdfPath)
        {
            var document = new Document();
            document.Info.Title = "Reporte de disponibilidad vmPing";
            document.Info.Author = "vmPing GLR";

            var normal = document.Styles["Normal"];
            normal.Font.Name = "Segoe UI";
            normal.Font.Size = 9;

            var section = document.AddSection();
            section.PageSetup.PageFormat = PageFormat.A4;
            section.PageSetup.Orientation = Orientation.Portrait;
            section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);

            var header = section.Headers.Primary.AddParagraph();
            header.Format.Font.Size = 8;
            header.Format.Font.Color = Colors.Gray;
            header.AddText("vmPing GLR - Reporte de disponibilidad");
            header.AddTab();
            header.AddText($"Generado: {summary.GeneratedLocal:dd/MM/yyyy HH:mm}");

            var footer = section.Footers.Primary.AddParagraph();
            footer.Format.Font.Size = 8;
            footer.Format.Font.Color = Colors.Gray;
            footer.Format.Alignment = ParagraphAlignment.Center;
            footer.AddText("Pagina ");
            footer.AddPageField();
            footer.AddText(" de ");
            footer.AddNumPagesField();

            var title = section.AddParagraph("Reporte de disponibilidad");
            title.Format.Font.Size = 20;
            title.Format.Font.Bold = true;
            title.Format.Font.Color = Colors.DarkBlue;
            title.Format.SpaceAfter = Unit.FromCentimeter(0.2);

            var subtitle = section.AddParagraph(
                $"Periodo: {summary.RangeStartLocal:dd/MM/yyyy HH:mm} a {summary.RangeEndLocal:dd/MM/yyyy HH:mm}");
            subtitle.Format.Font.Size = 10;
            subtitle.Format.Font.Color = Colors.Gray;
            subtitle.Format.SpaceAfter = Unit.FromCentimeter(0.5);

            AddSummaryTable(section, summary);
            AddOkHosts(section, summary);
            AddProblemHosts(section, summary);
            AddFailureDetails(section, summary);
            AddErrors(section, summary);
            AddInstability(section, summary);
            AddHourlyTable(section, summary);

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            renderer.PdfDocument.Save(pdfPath);
        }

        private static void AddSummaryTable(Section section, ReportSummary summary)
        {
            AddSectionHeading(section, "Resumen ejecutivo");

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            table.AddColumn(Unit.FromCentimeter(5));
            table.AddColumn(Unit.FromCentimeter(11.5));

            AddKeyValueRow(table, "Equipos monitoreados", summary.HostCount.ToString(CultureInfo.InvariantCulture));
            AddKeyValueRow(table, "Equipos OK", summary.OkHostCount.ToString(CultureInfo.InvariantCulture));
            AddKeyValueRow(table, "Equipos con problemas", summary.ProblemHostCount.ToString(CultureInfo.InvariantCulture));
            AddKeyValueRow(table, "Muestras totales", summary.TotalSamples.ToString("N0", CultureInfo.CurrentCulture));
            AddKeyValueRow(table, "Muestras fallidas", summary.FailedSamples.ToString("N0", CultureInfo.CurrentCulture));
            AddKeyValueRow(table, "Disponibilidad global", $"{summary.OverallAvailabilityPercent:0.00}%");
            AddKeyValueRow(table, "Criterio de OK", $"Disponibilidad >= {OkAvailabilityThreshold:0.00}% y menos de 6 cambios de estado");
        }

        private static void AddOkHosts(Section section, ReportSummary summary)
        {
            AddSectionHeading(section, "Equipos OK");

            var hosts = summary.Hosts.Where(h => h.IsOk).OrderBy(h => h.Hostname, StringComparer.OrdinalIgnoreCase).ToList();
            if (hosts.Count == 0)
            {
                section.AddParagraph("No hay equipos que cumplan el criterio de OK en el periodo.");
                return;
            }

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            AddColumn(table, 4.4, "Equipo");
            AddColumn(table, 4.9, "Alias / descripcion");
            AddColumn(table, 2.0, "Tipo");
            AddColumn(table, 2.2, "Disponib.");
            AddColumn(table, 2.4, "Lat. media");

            AddHeaderRow(table, "Equipo", "Alias / descripcion", "Tipo", "Disponib.", "Lat. media");
            foreach (var host in hosts)
            {
                var row = table.AddRow();
                SetCell(row, 0, host.Hostname);
                SetCell(row, 1, host.Alias);
                SetCell(row, 2, host.ProbeType);
                SetCell(row, 3, $"{host.AvailabilityPercent:0.00}%");
                SetCell(row, 4, $"{host.AverageLatencyMs:0} ms");
            }
        }

        private static void AddProblemHosts(Section section, ReportSummary summary)
        {
            AddSectionHeading(section, "Equipos con problemas");

            var hosts = summary.Hosts.Where(h => !h.IsOk).OrderBy(h => h.Hostname, StringComparer.OrdinalIgnoreCase).ToList();
            if (hosts.Count == 0)
            {
                section.AddParagraph("No se detectaron equipos con problemas en el periodo.");
                return;
            }

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            AddColumn(table, 4.0, "Equipo");
            AddColumn(table, 4.2, "Alias / descripcion");
            AddColumn(table, 1.8, "Tipo");
            AddColumn(table, 2.0, "Disponib.");
            AddColumn(table, 1.8, "Fallos");
            AddColumn(table, 1.8, "Cortes");
            AddColumn(table, 1.4, "Flaps");

            AddHeaderRow(table, "Equipo", "Alias / descripcion", "Tipo", "Disponib.", "Fallos", "Cortes", "Flaps");
            foreach (var host in hosts)
            {
                var row = table.AddRow();
                SetCell(row, 0, host.Hostname);
                SetCell(row, 1, host.Alias);
                SetCell(row, 2, host.ProbeType);
                SetCell(row, 3, $"{host.AvailabilityPercent:0.00}%");
                SetCell(row, 4, host.FailedSamples.ToString("N0", CultureInfo.CurrentCulture));
                SetCell(row, 5, host.FailureWindowCount.ToString(CultureInfo.InvariantCulture));
                SetCell(row, 6, host.StatusFlaps.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AddFailureDetails(Section section, ReportSummary summary)
        {
            AddSectionHeading(section, "Horarios de fallo");

            var windows = summary.Hosts
                .SelectMany(h => h.FailureWindows.Select(w => new { Host = h, Window = w }))
                .OrderBy(x => x.Window.StartUtc)
                .ToList();

            if (windows.Count == 0)
            {
                section.AddParagraph("No se registraron ventanas de fallo en el periodo.");
                return;
            }

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            AddColumn(table, 4.0, "Equipo");
            AddColumn(table, 3.8, "Inicio (local)");
            AddColumn(table, 3.8, "Fin (local)");
            AddColumn(table, 1.8, "Duracion");
            AddColumn(table, 1.8, "Muestras");
            AddColumn(table, 1.4, "Error");

            AddHeaderRow(table, "Equipo", "Inicio (local)", "Fin (local)", "Duracion", "Muestras", "Error");

            foreach (var item in windows.Take(MaxFailureRows))
            {
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(item.Window.StartUtc, TimeZoneInfo.Local);
                var endLocal = TimeZoneInfo.ConvertTimeFromUtc(item.Window.EndUtc, TimeZoneInfo.Local);
                var duration = endLocal - startLocal;
                var durationText = duration.TotalMinutes < 1
                    ? "< 1 min"
                    : $"{(int)duration.TotalMinutes} min";

                var row = table.AddRow();
                SetCell(row, 0, item.Host.Hostname);
                SetCell(row, 1, startLocal.ToString("dd/MM HH:mm:ss", CultureInfo.CurrentCulture));
                SetCell(row, 2, endLocal.ToString("dd/MM HH:mm:ss", CultureInfo.CurrentCulture));
                SetCell(row, 3, durationText);
                SetCell(row, 4, item.Window.FailedSamples.ToString(CultureInfo.InvariantCulture));
                SetCell(row, 5, CompactError(item.Window.LastError), 7);
            }

            if (windows.Count > MaxFailureRows)
            {
                var note = section.AddParagraph(
                    $"Se muestran las primeras {MaxFailureRows} ventanas de un total de {windows.Count}. Consulte la base de datos para el detalle completo.");
                note.Format.Font.Size = 8;
                note.Format.Font.Color = Colors.Gray;
            }
        }

        private static void AddErrors(Section section, ReportSummary summary)
        {
            AddSectionHeading(section, "Errores mas frecuentes");

            if (summary.Errors.Count == 0)
            {
                section.AddParagraph("No se registraron errores en el periodo.");
                return;
            }

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            AddColumn(table, 9.8, "Error");
            AddColumn(table, 3.0, "Ocurrencias");
            AddColumn(table, 3.2, "Equipos afect.");

            AddHeaderRow(table, "Error", "Ocurrencias", "Equipos afect.");
            foreach (var error in summary.Errors)
            {
                var row = table.AddRow();
                SetCell(row, 0, CompactError(error.Error, 75));
                SetCell(row, 1, error.Count.ToString("N0", CultureInfo.CurrentCulture));
                SetCell(row, 2, error.AffectedHosts.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AddInstability(Section section, ReportSummary summary)
        {
            AddSectionHeading(section, "Inestabilidad");

            var unstable = summary.Hosts
                .Where(h => h.StatusFlaps >= 6)
                .OrderByDescending(h => h.StatusFlaps)
                .ThenBy(h => h.Hostname, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unstable.Count == 0)
            {
                section.AddParagraph("No se detectaron equipos inestables (6 o mas cambios de estado).");
                return;
            }

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            AddColumn(table, 4.8, "Equipo");
            AddColumn(table, 5.6, "Alias / descripcion");
            AddColumn(table, 2.2, "Cambios");
            AddColumn(table, 2.4, "Disponib.");

            AddHeaderRow(table, "Equipo", "Alias / descripcion", "Cambios", "Disponib.");
            foreach (var host in unstable)
            {
                var row = table.AddRow();
                SetCell(row, 0, host.Hostname);
                SetCell(row, 1, host.Alias);
                SetCell(row, 2, host.StatusFlaps.ToString(CultureInfo.InvariantCulture));
                SetCell(row, 3, $"{host.AvailabilityPercent:0.00}%");
            }
        }

        private static void AddHourlyTable(Section section, ReportSummary summary)
        {
            AddSectionHeading(section, "Bitacora por hora");

            if (summary.Hourly.Count == 0)
            {
                section.AddParagraph("No hay datos para el periodo seleccionado.");
                return;
            }

            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Colors.LightGray;
            AddColumn(table, 3.6, "Hora local");
            AddColumn(table, 2.6, "Muestras");
            AddColumn(table, 2.6, "Fallidas");
            AddColumn(table, 2.6, "Equipos afect.");
            AddColumn(table, 2.6, "Disponib.");

            AddHeaderRow(table, "Hora local", "Muestras", "Fallidas", "Equipos afect.", "Disponib.");
            foreach (var hour in summary.Hourly)
            {
                var row = table.AddRow();
                SetCell(row, 0, hour.LocalHour.ToString("dd/MM/yyyy HH:00", CultureInfo.CurrentCulture));
                SetCell(row, 1, hour.TotalSamples.ToString("N0", CultureInfo.CurrentCulture));
                SetCell(row, 2, hour.FailedSamples.ToString("N0", CultureInfo.CurrentCulture));
                SetCell(row, 3, hour.AffectedHosts.ToString(CultureInfo.InvariantCulture));
                SetCell(row, 4, $"{hour.AvailabilityPercent:0.00}%");
            }
        }

        private static void AddSectionHeading(Section section, string text)
        {
            var paragraph = section.AddParagraph(text);
            paragraph.Format.Font.Size = 13;
            paragraph.Format.Font.Bold = true;
            paragraph.Format.Font.Color = Colors.DarkBlue;
            paragraph.Format.SpaceBefore = Unit.FromCentimeter(0.55);
            paragraph.Format.SpaceAfter = Unit.FromCentimeter(0.18);
            paragraph.Format.KeepWithNext = true;
        }

        private static void AddHeaderRow(Table table, params string[] headers)
        {
            var row = table.AddRow();
            row.HeadingFormat = true;
            row.Shading.Color = Colors.LightGray;
            row.Format.Font.Bold = true;
            for (var i = 0; i < headers.Length; i++)
            {
                SetCell(row, i, headers[i]);
            }
        }

        private static void AddKeyValueRow(Table table, string key, string value)
        {
            var row = table.AddRow();
            row.Cells[0].Format.Font.Bold = true;
            SetCell(row, 0, key);
            SetCell(row, 1, value);
        }

        private static void SetCell(Row row, int index, string text, int maxLength = 90)
        {
            var value = text ?? string.Empty;
            if (value.Length > maxLength)
            {
                value = value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
            }

            var paragraph = row.Cells[index].AddParagraph(value);
            paragraph.Format.Font.Size = 8;
            paragraph.Format.SpaceBefore = Unit.FromPoint(1);
            paragraph.Format.SpaceAfter = Unit.FromPoint(1);
        }

        private static void AddColumn(Table table, double centimeters, string _)
        {
            table.AddColumn(Unit.FromCentimeter(centimeters));
        }

        private static string CompactError(string error, int maxLength = 40)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return string.Empty;
            }

            var singleLine = error.Replace("\r", " ").Replace("\n", " ").Trim();
            return singleLine.Length <= maxLength
                ? singleLine
                : singleLine.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private static void SendEmail(ReportRequest request, ReportResult result)
        {
            if (string.IsNullOrWhiteSpace(ApplicationOptions.ReportSmtpHost))
            {
                throw new InvalidOperationException("Configure el servidor SMTP en Opciones > Reportes.");
            }

            var from = string.IsNullOrWhiteSpace(ApplicationOptions.ReportFromAddress)
                ? ApplicationOptions.ReportSmtpUser
                : ApplicationOptions.ReportFromAddress;

            if (string.IsNullOrWhiteSpace(from))
            {
                throw new InvalidOperationException("Configure la direccion remitente en Opciones > Reportes.");
            }

            var recipients = request.Recipients
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .ToList();

            if (recipients.Count == 0)
            {
                throw new InvalidOperationException("No hay destinatarios validos.");
            }

            using (var message = new MailMessage())
            {
                message.From = new MailAddress(from);
                foreach (var recipient in recipients)
                {
                    message.To.Add(recipient);
                }

                var summary = result.Summary;
                message.Subject = $"Reporte vmPing {summary.RangeStartLocal:dd/MM/yyyy HH:mm} - {summary.RangeEndLocal:dd/MM/yyyy HH:mm}";
                message.Body = BuildEmailBody(summary);
                message.IsBodyHtml = false;
                message.Attachments.Add(new Attachment(result.PdfPath));

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                using (var client = new SmtpClient(ApplicationOptions.ReportSmtpHost, ApplicationOptions.ReportSmtpPort))
                {
                    client.EnableSsl = ApplicationOptions.ReportSmtpUseSsl;
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                    client.UseDefaultCredentials = false;
                    client.Timeout = 60000;

                    if (!string.IsNullOrWhiteSpace(ApplicationOptions.ReportSmtpUser))
                    {
                        client.Credentials = new NetworkCredential(
                            ApplicationOptions.ReportSmtpUser,
                            ApplicationOptions.ReportSmtpPassword ?? string.Empty);
                    }

                    client.Send(message);
                }
            }
        }

        private static string BuildEmailBody(ReportSummary summary)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Reporte de disponibilidad vmPing");
            builder.AppendLine($"Periodo: {summary.RangeStartLocal:dd/MM/yyyy HH:mm} a {summary.RangeEndLocal:dd/MM/yyyy HH:mm}");
            builder.AppendLine();
            builder.AppendLine($"Equipos monitoreados: {summary.HostCount}");
            builder.AppendLine($"Equipos OK: {summary.OkHostCount}");
            builder.AppendLine($"Equipos con problemas: {summary.ProblemHostCount}");
            builder.AppendLine($"Disponibilidad global: {summary.OverallAvailabilityPercent:0.00}%");
            builder.AppendLine();
            builder.AppendLine("El detalle completo esta adjunto en el PDF.");
            return builder.ToString();
        }

        private static void RecordRun(ReportRequest request, string pdfPath, string status, string error)
        {
            HistoryService.RecordReportRun(
                request.StartLocal.ToUniversalTime(),
                request.EndLocal.ToUniversalTime(),
                request.Recipients,
                status,
                error);

            HistoryService.RecordAudit(
                "report-run",
                Path.GetFileName(pdfPath ?? string.Empty),
                $"status={status}; from={request.StartLocal:yyyy-MM-dd HH:mm}; to={request.EndLocal:yyyy-MM-dd HH:mm}; recipients={request.Recipients}");
        }
    }
}
