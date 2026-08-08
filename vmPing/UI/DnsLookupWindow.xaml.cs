using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class DnsLookupWindow : Window
    {
        private readonly ObservableCollection<DnsRecordResult> _results = new ObservableCollection<DnsRecordResult>();

        public DnsLookupWindow(string defaultDomain = "")
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            if (!string.IsNullOrEmpty(defaultDomain))
                txtDomain.Text = defaultDomain;

            dgDns.ItemsSource = _results;
        }

        private async void BtnQuery_Click(object sender, RoutedEventArgs e)
        {
            string domain = txtDomain.Text?.Trim();
            if (string.IsNullOrWhiteSpace(domain))
            {
                MessageBox.Show("Por favor ingrese un dominio o dirección IP.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedType = "A";
            var item = cmbRecordType.SelectedItem as ComboBoxItem;
            if (item != null)
            {
                string raw = item.Content.ToString();
                if (raw.StartsWith("A")) selectedType = "A";
                if (raw.StartsWith("AAAA")) selectedType = "AAAA";
                if (raw.StartsWith("MX")) selectedType = "MX";
                if (raw.StartsWith("TXT")) selectedType = "TXT";
                if (raw.StartsWith("PTR")) selectedType = "PTR";
                if (raw.StartsWith("NS")) selectedType = "NS";
                if (raw.StartsWith("TODOS")) selectedType = "ALL";
            }

            _results.Clear();
            btnQuery.IsEnabled = false;
            btnQuery.Content = "Consultando...";
            lblStatus.Text = $"Consultando registros {selectedType} para {domain}...";

            try
            {
                var list = await DnsQuery.QueryAsync(domain, selectedType);
                foreach (var r in list)
                {
                    _results.Add(r);
                }

                lblStatus.Text = list.Count > 0
                    ? $"Consulta completada. Se obtuvieron {list.Count} registros."
                    : "No se encontraron registros para la consulta realizada.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                btnQuery.IsEnabled = true;
                btnQuery.Content = "Consultar";
            }
        }
    }
}
