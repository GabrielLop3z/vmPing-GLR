using System;
using System.Diagnostics;
using System.Windows;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class ApiWindow : Window
    {
        public ApiWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
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

            Clipboard.SetText(ApiService.LocalUrl);
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
    }
}
