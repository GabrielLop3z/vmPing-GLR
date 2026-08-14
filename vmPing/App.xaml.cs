using System;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using AutoUpdaterDotNET;
using vmPing.Classes;

namespace vmPing
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Force software rendering. Otherwise application may have high GPU usage on some video cards.
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            // The following code snippet was taken from Stack Overflow answer here:
            // https://stackoverflow.com/questions/520115/stringformat-localization-issues-in-wpf/520334#520334

            // Ensure the current culture passed into bindings is the OS culture.
            // By default, WPF uses en-US as the culture, regardless of the system settings.
            FrameworkElement.LanguageProperty.OverrideMetadata(
                  typeof(FrameworkElement),
                  new FrameworkPropertyMetadata(
                      XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            ApplyTheme(ApplicationOptions.CurrentTheme);

            // Global exception handler to catch runtime errors
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Initialize AutoUpdater
            // El update.xml se sirve desde el CDN de jsDelivr porque raw.githubusercontent.com
            // no es accesible desde todas las redes y congelaba la app en la búsqueda manual.
            AutoUpdater.Mandatory = false; // No obligar a actualizar
            AutoUpdater.UpdateFormSize = new System.Drawing.Size(800, 600);
            // Mantener el formulario de actualización por encima de las demás ventanas (si no,
            // con la opción "siempre visible" vmPing lo tapa y parece que no pasa nada).
            AutoUpdater.TopMost = true;
            // vmPing se ejecuta desde una carpeta con permisos de usuario (no Program Files),
            // así que la actualización NO requiere elevación. Con RunUpdateAsAdmin = true (por defecto)
            // ZipExtractor pedía elevación UAC y, en equipos con política restrictiva o EDR (p. ej.
            // AppLocker/Cortex), Windows lo bloqueaba con "la directiva bloqueó ese programa"
            // (System.ComponentModel.Win32Exception) al relanzar el programa. Se fuerza false para
            // aplicar la actualización sin pedir permisos de administrador.
            AutoUpdater.RunUpdateAsAdmin = false;
            // Con Synchronous = false (por defecto) la revisión corre en segundo plano y no bloquea la UI.
            AutoUpdater.Start("https://cdn.jsdelivr.net/gh/GabrielLop3z/vmPing-GLR@main/update.xml");
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Create a friendly error message
            string errorMessage = $"Ocurrió un error inesperado en el sistema:\n\n{e.Exception.Message}\n\n" +
                                  $"Si el error persiste, por favor contacte al soporte.\n\n" +
                                  $"Detalles técnicos:\n{e.Exception.StackTrace}";

            // Show error window (using MessageBox for simplicity/reliability during a crash)
            MessageBox.Show(errorMessage, "Error del Sistema - vmPing", MessageBoxButton.OK, MessageBoxImage.Error);

            // Prevent default crash behavior
            e.Handled = true;
        }

        public static void ApplyTheme(ApplicationOptions.AppTheme theme)
        {
            var app = (App)Application.Current;
            var themeUri = new Uri("ResourceDictionaries/Themes.xaml", UriKind.Relative);
            var themesResource = new ResourceDictionary { Source = themeUri };

            ResourceDictionary selectedTheme;
            switch (theme)
            {
                case ApplicationOptions.AppTheme.Dark:
                    selectedTheme = (ResourceDictionary)themesResource["DarkTheme"];
                    break;
                case ApplicationOptions.AppTheme.Crystal:
                    selectedTheme = (ResourceDictionary)themesResource["CrystalTheme"];
                    break;
                case ApplicationOptions.AppTheme.Colorful:
                    selectedTheme = (ResourceDictionary)themesResource["ColorfulTheme"];
                    break;
                case ApplicationOptions.AppTheme.Ocean:
                    selectedTheme = (ResourceDictionary)themesResource["OceanTheme"];
                    break;
                case ApplicationOptions.AppTheme.Hacking:
                    selectedTheme = (ResourceDictionary)themesResource["HackingTheme"];
                    break;
                case ApplicationOptions.AppTheme.Sunset:
                    selectedTheme = (ResourceDictionary)themesResource["SunsetTheme"];
                    break;
                case ApplicationOptions.AppTheme.Red:
                    selectedTheme = (ResourceDictionary)themesResource["RedTheme"];
                    break;
                default:
                    selectedTheme = (ResourceDictionary)themesResource["LightTheme"];
                    break;
            }

            // Remove existing theme resources if any
            foreach (var key in selectedTheme.Keys)
            {
                app.Resources[key] = selectedTheme[key];
            }
        }

        // DEBUG: Use this for testing alternate locales.
        //public App()
        //{
        //    vmPing.Properties.Strings.Culture = new System.Globalization.CultureInfo("en-GB");
        //}
    }
}
