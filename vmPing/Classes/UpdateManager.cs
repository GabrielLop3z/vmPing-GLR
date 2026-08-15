using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using vmPing.UI;

namespace vmPing.Classes
{
    public class UpdateInfo
    {
        public Version Version { get; set; }
        public string Url { get; set; }
        public bool Mandatory { get; set; }
        public string Changelog { get; set; }
    }

    internal static class UpdateManager
    {
        private const string UpdateXmlUrl = "https://raw.githubusercontent.com/GabrielLop3z/vmPing-GLR/main/update.xml";
        private const string StateFileName = "update_state.txt";
        private const int RemindLaterDays = 1;

        private static string StateFilePath => Path.Combine(
            Path.GetDirectoryName(Configuration.FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            StateFileName);

        public static Version CurrentVersion => typeof(UpdateManager).Assembly.GetName().Version;

        private class UpdateState
        {
            public string SkipVersion { get; set; }
            public long? RemindAfterTicks { get; set; }
        }

        public static async void CheckForUpdate(bool showUpToDateMessage)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string xml;
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "vmPing-GLR");
                    xml = await client.DownloadStringTaskAsync(UpdateXmlUrl);
                }

                var update = ParseUpdateInfo(xml);
                if (update == null || update.Version <= CurrentVersion)
                {
                    if (showUpToDateMessage)
                    {
                        Util.ShowInfo("vmPing está actualizado.");
                    }
                    return;
                }

                var state = LoadState();
                if (!update.Mandatory)
                {
                    if (state.SkipVersion == update.Version.ToString())
                    {
                        return;
                    }
                    if (state.RemindAfterTicks.HasValue && DateTime.UtcNow.Ticks < state.RemindAfterTicks.Value)
                    {
                        return;
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var window = new UpdateWindow(update);
                    if (Application.Current.MainWindow != null)
                    {
                        window.Owner = Application.Current.MainWindow;
                    }
                    window.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                if (showUpToDateMessage)
                {
                    Util.ShowError($"No se pudo comprobar si hay actualizaciones disponibles.\n\n{ex.Message}");
                }
            }
        }

        private static UpdateInfo ParseUpdateInfo(string xml)
        {
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(xml);
                var item = doc.Element("item");
                if (item == null)
                {
                    return null;
                }

                var versionElement = item.Element("version");
                var urlElement = item.Element("url");
                if (versionElement == null || urlElement == null)
                {
                    return null;
                }

                if (!Version.TryParse(versionElement.Value.Trim(), out var version))
                {
                    return null;
                }

                var mandatory = false;
                var mandatoryElement = item.Element("mandatory");
                if (mandatoryElement != null)
                {
                    bool.TryParse(mandatoryElement.Value, out mandatory);
                }

                var changelog = item.Element("changelog")?.Value?.Trim();

                return new UpdateInfo
                {
                    Version = version,
                    Url = urlElement.Value.Trim(),
                    Mandatory = mandatory,
                    Changelog = changelog
                };
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string> DownloadToFileAsync(string url, string destination, IProgress<int> percent)
        {
            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += (s, e) => percent?.Report(e.ProgressPercentage);
                await client.DownloadFileTaskAsync(url, destination);
            }
            return destination;
        }

        public static bool ApplyUpdate(string zipPath)
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var updateDir = Path.Combine(appDir, "update");
                if (Directory.Exists(updateDir))
                {
                    Directory.Delete(updateDir, true);
                }

                ZipFile.ExtractToDirectory(zipPath, updateDir);

                // Verify the archive actually contains the application.
                if (!File.Exists(Path.Combine(updateDir, "vmPing.exe")))
                {
                    throw new Exception("El archivo de actualización no contiene vmPing.exe.");
                }

                var batchContent = new StringBuilder();
                batchContent.AppendLine("@echo off");
                batchContent.AppendLine("set /a tries=0");
                batchContent.AppendLine(":wait");
                batchContent.AppendLine("set /a tries+=1");
                batchContent.AppendLine("if %tries% GTR 120 goto force");
                batchContent.AppendLine("tasklist /FI \"IMAGENAME eq vmPing.exe\" 2>nul | find /I \"vmPing.exe\" >nul");
                batchContent.AppendLine("if not errorlevel 1 (");
                batchContent.AppendLine("  ping -n 2 127.0.0.1 >nul");
                batchContent.AppendLine("  goto wait");
                batchContent.AppendLine(")");
                batchContent.AppendLine("goto copy");
                batchContent.AppendLine(":force");
                batchContent.AppendLine("taskkill /F /IM vmPing.exe >nul 2>&1");
                batchContent.AppendLine("ping -n 2 127.0.0.1 >nul");
                batchContent.AppendLine(":copy");
                batchContent.AppendLine("xcopy /y /e /q /i \"%~dp0update\\*.*\" \"%~dp0\" >nul 2>&1");
                batchContent.AppendLine("rd /s /q \"%~dp0update\"");
                batchContent.AppendLine("del /q \"%~dp0AutoUpdater.NET.dll\" >nul 2>&1");
                batchContent.AppendLine("start \"\" \"%~dp0vmPing.exe\"");
                batchContent.AppendLine("del \"%~f0\"");
                batchContent.AppendLine("exit /b 0");

                var batchPath = Path.Combine(appDir, "update.cmd");
                File.WriteAllText(batchPath, batchContent.ToString(), Encoding.Default);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{batchPath}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = appDir
                };
                Process.Start(psi);

                // Exit the application so the batch script can replace the files.
                Application.Current.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
                return true;
            }
            catch (Exception ex)
            {
                Util.ShowError($"No se pudo aplicar la actualización: {ex.Message}");
                return false;
            }
        }

        public static void SkipVersion(Version version)
        {
            var state = LoadState();
            state.SkipVersion = version.ToString();
            SaveState(state);
        }

        public static void RemindLater()
        {
            var state = LoadState();
            state.RemindAfterTicks = DateTime.UtcNow.AddDays(RemindLaterDays).Ticks;
            SaveState(state);
        }

        private static UpdateState LoadState()
        {
            var state = new UpdateState();
            try
            {
                if (File.Exists(StateFilePath))
                {
                    var lines = File.ReadAllLines(StateFilePath);
                    if (lines.Length > 0)
                    {
                        state.SkipVersion = lines[0];
                    }
                    if (lines.Length > 1 && long.TryParse(lines[1], out var ticks))
                    {
                        state.RemindAfterTicks = ticks;
                    }
                }
            }
            catch
            {
                // Best effort.
            }
            return state;
        }

        private static void SaveState(UpdateState state)
        {
            try
            {
                var dir = Path.GetDirectoryName(StateFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllLines(StateFilePath, new[]
                {
                    state.SkipVersion ?? string.Empty,
                    state.RemindAfterTicks?.ToString() ?? string.Empty
                });
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
