using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class ApiWindow : Window
    {
        private readonly DispatcherTimer _copyFeedbackTimer;

        public ApiWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _copyFeedbackTimer.Tick += (s, e) =>
            {
                _copyFeedbackTimer.Stop();
                lblCopyFeedback.Opacity = 0;
            };

            RefreshStatus();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // The API can be started or stopped from the Options window while
            // this window is open, so refresh whenever it regains focus.
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            var running = ApiService.IsRunning;

            lblStatus.Text = running
                ? "En ejecución"
                : "Detenida" + (string.IsNullOrWhiteSpace(ApiService.LastError) ? string.Empty : " — " + ApiService.LastError);
            lblStatus.Foreground = running
                ? (System.Windows.Media.Brush)FindResource("Theme.Status.Up.Foreground")
                : (System.Windows.Media.Brush)FindResource("Theme.Status.Down.Foreground");

            lblLocalUrl.Text = running ? ApiService.LocalUrl : "Active la API en Opciones → API";
            lblLanUrl.Text = running
                ? (ApiService.IsListeningOnLan
                    ? ApiService.BaseUrl
                    : "Solo este equipo (127.0.0.1)")
                : "-";

            lblKey.Text = string.IsNullOrWhiteSpace(ApplicationOptions.ApiKey)
                ? "(sin clave — solo acceso local)"
                : new string('•', Math.Min(16, ApplicationOptions.ApiKey.Length));

            lblDatabase.Text = string.IsNullOrWhiteSpace(HistoryService.DatabasePath)
                ? "Sin inicializar"
                : HistoryService.DatabasePath;

            txtEndpoints.Text =
                "GET /api/health                     Estado de la API y base de datos" + Environment.NewLine +
                "GET /api/hosts                      Hosts monitoreados en vivo" + Environment.NewLine +
                "GET /api/hosts/{host}               Detalle + historial de un host" + Environment.NewLine +
                "GET /api/ping?host=IP               Ping ICMP/TCP bajo demanda" + Environment.NewLine +
                "GET /api/samples?host=IP&from=-7d   Muestras históricas (SQLite)" + Environment.NewLine +
                "GET /api/events?from=-7d            Cambios de estado" + Environment.NewLine +
                "GET /api/stats?from=today           Disponibilidad agregada" + Environment.NewLine +
                "GET /api/daily?host=IP&date=FECHA   Resumen por hora de un día" + Environment.NewLine +
                "GET /api/inventory                  Inventario de equipos" + Environment.NewLine +
                "GET /api/openapi.json               Especificación OpenAPI" + Environment.NewLine +
                Environment.NewLine +
                "Ejemplo:" + Environment.NewLine +
                "curl \"http://127.0.0.1:8099/api/ping?host=10.1.1.50&count=2\"" + Environment.NewLine +
                Environment.NewLine +
                "Si la clave API está configurada, agregue la cabecera:" + Environment.NewLine +
                "X-Api-Key: <su clave>   (o el parámetro ?key=<su clave>)";
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!ApiService.IsRunning)
            {
                MessageBox.Show("La API está detenida. Actívela en Opciones → API.", "API HTTP",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Clipboard.SetText(ApiService.LocalUrl);
                lblCopyFeedback.Opacity = 1;
                _copyFeedbackTimer.Stop();
                _copyFeedbackTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo copiar la URL: " + ex.Message, "API HTTP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (!ApiService.IsRunning)
            {
                MessageBox.Show("La API está detenida. Actívela en Opciones → API.", "API HTTP",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(ApiService.LocalUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir el navegador: " + ex.Message, "API HTTP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }
    }
}
