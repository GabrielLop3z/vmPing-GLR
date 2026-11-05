using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class ReportPdfWindow : Window
    {
        private string _lastPdfPath;
        private bool _isBusy;

        public ReportPdfWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            LoadDefaults();
        }

        private void LoadDefaults()
        {
            var now = DateTime.Now;
            var start = now.Date.AddHours(Math.Max(0, Math.Min(23, ApplicationOptions.ReportDefaultStartHour)));
            var end = now.Date.AddHours(Math.Max(0, Math.Min(23, ApplicationOptions.ReportDefaultEndHour))).AddMinutes(59);

            if (now < start)
            {
                start = now.Date.AddDays(-1).AddHours(ApplicationOptions.ReportDefaultStartHour);
                end = now.Date.AddDays(-1).AddHours(ApplicationOptions.ReportDefaultEndHour).AddMinutes(59);
            }

            txtFrom.Text = start.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            txtTo.Text = end.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            txtWorkStartHour.Text = ApplicationOptions.ReportDefaultStartHour.ToString();
            txtWorkEndHour.Text = ApplicationOptions.ReportDefaultEndHour.ToString();
            txtRecipients.Text = ApplicationOptions.ReportRecipients ?? string.Empty;
            chkSendEmail.IsChecked = !string.IsNullOrWhiteSpace(txtRecipients.Text);
        }

        private void QuickLastHour_Click(object sender, RoutedEventArgs e)
        {
            var end = DateTime.Now;
            var start = end.AddHours(-1);
            txtFrom.Text = start.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            txtTo.Text = end.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            chkOnlyWorkHours.IsChecked = false;
        }

        private void QuickToday_Click(object sender, RoutedEventArgs e)
        {
            SetQuickRange(DateTime.Today, DateTime.Today);
        }

        private void QuickYesterday_Click(object sender, RoutedEventArgs e)
        {
            var yesterday = DateTime.Today.AddDays(-1);
            SetQuickRange(yesterday, yesterday);
        }

        private void QuickWeek_Click(object sender, RoutedEventArgs e)
        {
            SetQuickRange(DateTime.Today.AddDays(-6), DateTime.Today);
        }

        private void SetQuickRange(DateTime startDate, DateTime endDate)
        {
            txtFrom.Text = startDate.AddHours(ApplicationOptions.ReportDefaultStartHour)
                .ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            txtTo.Text = endDate.AddHours(ApplicationOptions.ReportDefaultEndHour).AddMinutes(59)
                .ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
            txtWorkStartHour.Text = ApplicationOptions.ReportDefaultStartHour.ToString();
            txtWorkEndHour.Text = ApplicationOptions.ReportDefaultEndHour.ToString();
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            await GenerateAsync(sendEmail: false);
        }

        private async void BtnGenerateSend_Click(object sender, RoutedEventArgs e)
        {
            await GenerateAsync(sendEmail: true);
        }

        private async Task GenerateAsync(bool sendEmail)
        {
            if (_isBusy)
            {
                return;
            }

            if (!TryBuildRequest(sendEmail, out var request, out var error))
            {
                MessageBox.Show(error, "Reporte PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true);
            txtStatus.Text = sendEmail ? "Generando y enviando reporte..." : "Generando reporte...";
            txtSummary.Text = "Procesando historial. Esto puede tardar unos segundos.";

            try
            {
                var result = await Task.Run(() => ReportService.GenerateAndSend(request));
                if (!result.Success)
                {
                    txtStatus.Text = "Error al generar el reporte.";
                    txtSummary.Text = result.Error ?? "Error desconocido.";
                    MessageBox.Show(result.Error ?? "Error desconocido.", "Reporte PDF",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _lastPdfPath = result.PdfPath;
                btnOpenPdf.IsEnabled = true;
                btnOpenFolder.IsEnabled = true;

                var summary = result.Summary;
                txtStatus.Text = sendEmail
                    ? "Reporte generado y enviado correctamente."
                    : "Reporte generado correctamente.";

                txtSummary.Text =
                    $"Periodo: {summary.RangeStartLocal:dd/MM/yyyy HH:mm} a {summary.RangeEndLocal:dd/MM/yyyy HH:mm}\n" +
                    $"Equipos monitoreados: {summary.HostCount}  |  OK: {summary.OkHostCount}  |  Con problemas: {summary.ProblemHostCount}\n" +
                    $"Muestras: {summary.TotalSamples:N0}  |  Fallidas: {summary.FailedSamples:N0}  |  Disponibilidad global: {summary.OverallAvailabilityPercent:0.00}%\n" +
                    $"PDF: {result.PdfPath}\n" +
                    (sendEmail ? $"Enviado a: {request.Recipients}" : "Correo no solicitado.");
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error inesperado.";
                txtSummary.Text = ex.Message;
                MessageBox.Show(ex.Message, "Reporte PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool TryBuildRequest(bool sendEmail, out ReportRequest request, out string error)
        {
            request = null;
            error = null;

            if (!TryParseDateTime(txtFrom.Text, out var start))
            {
                error = "La fecha/hora inicial no es valida. Use el formato dd/MM/yyyy HH:mm.";
                return false;
            }

            if (!TryParseDateTime(txtTo.Text, out var end))
            {
                error = "La fecha/hora final no es valida. Use el formato dd/MM/yyyy HH:mm.";
                return false;
            }

            if (end <= start)
            {
                error = "La fecha/hora final debe ser posterior a la inicial.";
                return false;
            }

            if ((end - start).TotalDays > 31)
            {
                error = "El periodo maximo por reporte es de 31 dias. Genere varios reportes para rangos mayores.";
                return false;
            }

            var startHour = ApplicationOptions.ReportDefaultStartHour;
            var endHour = ApplicationOptions.ReportDefaultEndHour;
            if (chkOnlyWorkHours.IsChecked == true)
            {
                if (!int.TryParse(txtWorkStartHour.Text, out startHour) || startHour < 0 || startHour > 23)
                {
                    error = "La hora inicial del horario laboral debe estar entre 0 y 23.";
                    return false;
                }

                if (!int.TryParse(txtWorkEndHour.Text, out endHour) || endHour < 0 || endHour > 23)
                {
                    error = "La hora final del horario laboral debe estar entre 0 y 23.";
                    return false;
                }
            }

            var recipients = (txtRecipients.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(recipients))
            {
                recipients = ApplicationOptions.ReportRecipients ?? string.Empty;
            }

            if (sendEmail)
            {
                if (string.IsNullOrWhiteSpace(recipients))
                {
                    error = "Indique al menos un destinatario para enviar el reporte.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(ApplicationOptions.ReportSmtpHost))
                {
                    error = "Configure el servidor SMTP en Opciones > Reportes.";
                    return false;
                }
            }

            request = new ReportRequest
            {
                StartLocal = start,
                EndLocal = end,
                OnlyWorkHours = chkOnlyWorkHours.IsChecked == true,
                StartHour = startHour,
                EndHour = endHour,
                Recipients = recipients,
                SendEmail = sendEmail,
                OutputDirectory = Path.Combine(
                    Path.GetDirectoryName(Configuration.FilePath) ?? ".",
                    "reports")
            };

            return true;
        }

        private static bool TryParseDateTime(string text, out DateTime value)
        {
            var formats = new[]
            {
                "dd/MM/yyyy HH:mm",
                "d/M/yyyy H:mm",
                "dd/MM/yyyy H:mm",
                "d/M/yyyy HH:mm",
                "dd-MM-yyyy HH:mm",
                "yyyy-MM-dd HH:mm"
            };

            return DateTime.TryParseExact(
                (text ?? string.Empty).Trim(),
                formats,
                CultureInfo.CurrentCulture,
                DateTimeStyles.None,
                out value)
                || DateTime.TryParse((text ?? string.Empty).Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out value);
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            btnGenerate.IsEnabled = !busy;
            btnGenerateSend.IsEnabled = !busy;
        }

        private void BtnOpenPdf_Click(object sender, RoutedEventArgs e)
        {
            OpenPath(_lastPdfPath, false);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenPath(_lastPdfPath, true);
        }

        private void OpenPath(string path, bool openFolder)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("El archivo ya no esta disponible.", "Reporte PDF",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (openFolder)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                    {
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Reporte PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
