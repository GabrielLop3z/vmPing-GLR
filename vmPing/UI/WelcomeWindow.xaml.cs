using System;
using System.Diagnostics;
using System.Windows;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class WelcomeWindow : Window
    {
        public static WelcomeWindow _OpenWindow = null;

        public WelcomeWindow()
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;

            Version version = typeof(MainWindow).Assembly.GetName().Version;
            VersionText.Text = $"Versión {version.Major}.{version.Minor}.{version.Build}";
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            }
            catch
            {
                // TODO
            }
            finally
            {
                e.Handled = true;
            }
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // If the user unchecks the box, don't show the welcome window again.
            if (ShowOnStartupCheckBox.IsChecked == false)
            {
                ApplicationOptions.IsWelcomeShown = true;
                Configuration.Save();
            }
            _OpenWindow = null;
        }
    }
}
