using System.Windows;
using System.Windows.Input;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class EditAliasWindow : Window
    {
        private readonly string _hostname;
        private Probe _probe;

        public EditAliasWindow(Probe pingItem)
        {
            InitializeComponent();
            _probe = pingItem;
            _hostname = pingItem.Hostname;

            Hostname.Text = _hostname;
            NewAlias.Text = pingItem.Alias;
            NewCategory.Text = pingItem.Category; // Load current category
            NewAlias.SelectAll();

            Loaded += (sender, e) => MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        public EditAliasWindow(string hostname, string alias)
        {
            InitializeComponent();

            _hostname = hostname;

            Hostname.Text = _hostname;
            NewAlias.Text = alias;
            NewAlias.SelectAll();
            
            // Disable Category editing if no probe instance is linked (though typically one is)
            // Or we could try to find it, but for now let's assume this legacy constructor might not support category
            NewCategory.IsEnabled = false; 

            // Set initial keyboard focus.
            Loaded += (sender, e) => MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string newAlias = NewAlias.Text?.Trim();
            string newCategory = NewCategory.Text?.Trim();

            // Update Alias
            if (string.IsNullOrWhiteSpace(newAlias))
            {
                Alias.Delete(_hostname);
            }
            else
            {
                Alias.Add(_hostname, newAlias);
            }

            // Update Category if we have a probe reference
            if (_probe != null)
            {
                 _probe.Category = string.IsNullOrWhiteSpace(newCategory) ? "General" : newCategory;
            }

            DialogResult = true;
        }
    }
}
