using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class MultiInputWindow : Window
    {
        // Constants for hiding minimize and maximize buttons.
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBOX = 0x10000; //maximize button
        private const int WS_MINIMIZEBOX = 0x20000; //minimize button

        // Aliases read from an imported CSV file. They are persisted by the caller
        // only when the dialog is accepted, so Cancel has no side effects.
        public Dictionary<string, string> ImportedAliases { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<string> Addresses
        {
            get
            {
                // Split and trim multi-address text. Split occurs on both newlines and commas.
                // Return as a list with empty entries removed.
                return MultiAddress.Text
                    .Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }
        }

        public MultiInputWindow(List<string> addresses = null)
        {
            InitializeComponent();

            // Set initial keyboard focus to text box.
            Loaded += (sender, e) => MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

            // Pre-populate textbox if any addresses were supplied.
            if (addresses != null
                && addresses.Count > 0
                && !addresses.All(x => string.IsNullOrWhiteSpace(x))
                )
            {
                // Convert list to multiline string and select all text.
                MultiAddress.Text = string.Join(Environment.NewLine, addresses);
                MultiAddress.SelectAll();
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void MultiAddress_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void MultiAddress_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            // Get paths of dropped files.
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0)
            {
                return;
            }

            // Only 1 file drop is supported.
            if (paths.Length > 1)
            {
                ShowError("Please drop only one file at a time.");
                return;
            }

            LoadFile(paths[0]);
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Host lists (*.csv;*.txt)|*.csv;*.txt|CSV with aliases (*.csv)|*.csv|Text (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Import Host List (with aliases)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadFile(openFileDialog.FileName);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV with aliases (*.csv)|*.csv|Text (*.txt)|*.txt",
                Title = "Export Host List (with aliases)",
                FileName = "vmPing_Hosts.csv",
                FilterIndex = 1
            };

            if (saveFileDialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var hosts = Addresses;
                bool asText = saveFileDialog.FilterIndex == 2
                    || string.Equals(Path.GetExtension(saveFileDialog.FileName), ".txt", StringComparison.OrdinalIgnoreCase);

                if (asText)
                {
                    // Backwards-compatible plain list: one host per line.
                    File.WriteAllText(saveFileDialog.FileName,
                        string.Join(Environment.NewLine, hosts), new UTF8Encoding(true));
                    SetStatus($"Exported {hosts.Count} hosts");
                    return;
                }

                // CSV with the alias stored for each host (host,alias).
                var aliases = Alias.GetAll();
                var sb = new StringBuilder();
                sb.AppendLine("host,alias");

                foreach (var host in hosts)
                {
                    aliases.TryGetValue(host.ToLowerInvariant(), out var alias);
                    sb.AppendLine($"{CsvField(host)},{CsvField(alias)}");
                }

                File.WriteAllText(saveFileDialog.FileName, sb.ToString(), new UTF8Encoding(true));
                SetStatus($"Exported {hosts.Count} hosts with aliases");
            }
            catch (Exception ex)
            {
                ShowError($"File could not be saved: {ex.Message}");
            }
        }

        private void LoadFile(string path)
        {
            const long MaxSizeInBytes = 1024 * 1024;

            try
            {
                // Check filesize.
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > MaxSizeInBytes)
                {
                    ShowError($"\"{path}\" is too large. The maximum file size is {MaxSizeInBytes / 1024} KB.");
                    return;
                }

                bool csvFile = string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);
                var hosts = new List<string>();
                int aliasCount = 0;
                bool firstContentLine = true;

                foreach (var rawLine in File.ReadLines(path))
                {
                    var line = rawLine.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//"))
                    {
                        continue;
                    }

                    // A .csv file (or any line using ; or tab) is treated as host + alias.
                    // Plain .txt files keep the old behavior: one host per line.
                    List<string> fields;
                    if (csvFile || line.IndexOf(';') >= 0 || line.IndexOf('\t') >= 0)
                    {
                        fields = SplitFields(line);
                    }
                    else
                    {
                        fields = new List<string> { line };
                    }

                    var host = fields[0].Trim();
                    var alias = fields.Count > 1 ? fields[1].Trim() : string.Empty;

                    if (firstContentLine && IsHeaderRow(host))
                    {
                        firstContentLine = false;
                        continue;
                    }

                    firstContentLine = false;

                    // Valid hosts begin with a letter, a number, or '[' (IPv6).
                    if (host.Length == 0 || (!char.IsLetterOrDigit(host[0]) && host[0] != '['))
                    {
                        continue;
                    }

                    hosts.Add(host);

                    if (!string.IsNullOrWhiteSpace(alias))
                    {
                        ImportedAliases[host] = alias;
                        aliasCount++;
                    }
                }

                MultiAddress.Text = string.Join(Environment.NewLine, hosts);
                SetStatus(aliasCount > 0
                    ? $"Imported {hosts.Count} hosts and {aliasCount} aliases"
                    : $"Imported {hosts.Count} hosts");
            }
            catch (Exception ex)
            {
                ShowError($"File could not be opened: {ex.Message}");
            }
        }

        private static bool IsHeaderRow(string value)
        {
            switch ((value ?? string.Empty).Trim().Trim('"').ToLowerInvariant())
            {
                case "host":
                case "hostname":
                case "equipo":
                case "nombre":
                case "name":
                case "ip":
                case "direccion":
                    return true;
                default:
                    return false;
            }
        }

        // Splits a CSV line supporting quoted fields (RFC 4180) and ; or tab separators.
        private static List<string> SplitFields(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (!inQuotes && (c == ',' || c == ';' || c == '\t'))
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result;
        }

        private static string CsvField(string value)
        {
            value = value ?? string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private void SetStatus(string message)
        {
            if (lblStatus != null)
            {
                lblStatus.Text = message;
            }
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // Hide minimize and maximize buttons.
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == null)
            {
                return;
            }

            SetWindowLong(handle, GWL_STYLE, GetWindowLong(handle, GWL_STYLE) & ~WS_MAXIMIZEBOX & ~WS_MINIMIZEBOX);
        }

        private void ShowError(string message)
        {
            var dialog = DialogWindow.ErrorWindow(message);
            dialog.Owner = this;
            dialog.ShowDialog();
        }
    }
}
