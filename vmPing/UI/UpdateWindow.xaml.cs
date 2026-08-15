using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _update;
        private string _downloadPath;

        public UpdateWindow(UpdateInfo update)
        {
            InitializeComponent();
            _update = update;
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            CurrentVersionText.Text = FormatVersion(UpdateManager.CurrentVersion);
            NewVersionText.Text = update.Version.ToString();

            if (!string.IsNullOrWhiteSpace(update.Changelog))
            {
                ChangelogContainer.Visibility = Visibility.Visible;
                ChangelogBox.Text = update.Changelog;
            }

            if (update.Mandatory)
            {
                SkipButton.Visibility = Visibility.Collapsed;
                RemindButton.Visibility = Visibility.Collapsed;
            }
        }

        private static string FormatVersion(Version version)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateManager.SkipVersion(_update.Version);
            Close();
        }

        private void RemindButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateManager.RemindLater();
            Close();
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateButton.IsEnabled = false;
            SkipButton.IsEnabled = false;
            RemindButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;

            try
            {
                _downloadPath = Path.Combine(Path.GetTempPath(), "vmPing_update.zip");
                if (File.Exists(_downloadPath))
                {
                    File.Delete(_downloadPath);
                }

                var progress = new Progress<int>(percent =>
                {
                    DownloadProgressBar.Value = percent;
                    ProgressStatusText.Text = $"Descargando actualización... {percent}%";
                });

                await UpdateManager.DownloadToFileAsync(_update.Url, _downloadPath, progress);

                ProgressStatusText.Text = "Aplicando actualización...";
                UpdateManager.ApplyUpdate(_downloadPath);
            }
            catch (Exception ex)
            {
                ProgressStatusText.Text = "No se pudo descargar la actualización.";
                UpdateButton.IsEnabled = true;
                SkipButton.IsEnabled = true;
                RemindButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
                Util.ShowError($"No se pudo descargar la actualización.\n\n{ex.Message}");
            }
        }
    }
}
