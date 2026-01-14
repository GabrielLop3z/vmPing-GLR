using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using vmPing.Classes;
using vmPing.Properties;

namespace vmPing.UI
{
    public partial class ManageAliasesWindow : Window
    {
        // Imports and constants for hiding minimize and maximize buttons.
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_STYLE = -16;
        private const int WS_MAXIMIZEBOX = 0x10000; //maximize button
        private const int WS_MINIMIZEBOX = 0x20000; //minimize button

        public ManageAliasesWindow()
        {
            InitializeComponent();
            RefreshAliasList();
        }

        public void RefreshAliasList()
        {
            var aliases = Alias.GetAll()
                .OrderBy(alias => alias.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Aliases.ItemsSource = aliases;
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAlias();
            if (selected == null)
            {
                return;
            }

            var dialogWindow = new DialogWindow(
                icon: DialogWindow.DialogIcon.Warning,
                title: Strings.DialogTitle_ConfirmDelete,
                body: $"{Strings.ManageAliases_Warn_DeleteA} {selected.Value.Value} {Strings.ManageAliases_Warn_DeleteB}",
                confirmationText: Strings.DialogButton_Remove,
                isCancelButtonVisible: true)
            {
                Owner = this
            };

            if (dialogWindow.ShowDialog() == true)
            {
                Alias.Delete(selected.Value.Key);
                RefreshAliasList();
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedAlias();
            if (selected == null)
            {
                return;
            }

            var editWindow = new EditAliasWindow(selected.Value.Key, selected.Value.Value)
            {
                Owner = this
            };

            if (editWindow.ShowDialog() == true)
            {
                RefreshAliasList();
            }
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            var newWindow = new NewAliasWindow
            {
                Owner = this
            };

            if (newWindow.ShowDialog() == true)
            {
                RefreshAliasList();
            }
        }

        private KeyValuePair<string, string>? GetSelectedAlias()
        {
            var selected = Aliases.SelectedItem as KeyValuePair<string, string>?;
            return selected.HasValue ? selected : null;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // Hide minimize and maximize buttons.
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == null)
            {
                return;
            }

            SetWindowLong(handle, GWL_STYLE, GetWindowLong(handle, GWL_STYLE) & ~WS_MAXIMIZEBOX & ~WS_MINIMIZEBOX);
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "vmPing Aliases (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "Import Aliases"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string content = System.IO.File.ReadAllText(openFileDialog.FileName);
                    int count = Alias.Import(content);
                    if (count >= 0)
                    {
                        RefreshAliasList();
                        Util.ShowInfo($"Successfully imported {count} alias(es).");
                    }
                    else
                    {
                        Util.ShowError("Failed to import aliases. The file may be invalid.");
                    }
                }
                catch (Exception ex)
                {
                    Util.ShowError($"Error importing aliases: {ex.Message}");
                }
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (Aliases.SelectedItems.Count == 0) return;

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "vmPing Aliases (*.xml)|*.xml",
                Title = "Export Aliases",
                FileName = "vmPing_Aliases.xml"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string content = Alias.Export(Aliases.SelectedItems);
                    System.IO.File.WriteAllText(saveFileDialog.FileName, content);
                    Util.ShowInfo("Aliases exported successfully.");
                }
                catch (Exception ex)
                {
                    Util.ShowError($"Error exporting aliases: {ex.Message}");
                }
            }
        }
    }
}
