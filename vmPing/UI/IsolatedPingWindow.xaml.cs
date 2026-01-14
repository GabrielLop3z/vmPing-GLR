using System;
using System.Windows;
using System.Windows.Controls;
using vmPing.Classes;

namespace vmPing.UI
{
    public partial class IsolatedPingWindow : Window
    {
        public IsolatedPingWindow(Probe pingItem)
        {
            InitializeComponent();
            Topmost = ApplicationOptions.IsAlwaysOnTopEnabled;
            pingItem.IsolatedWindow = this;
            DataContext = pingItem;
        }
        private void Window_Closed(object sender, EventArgs e)
        {
            (DataContext as Probe).IsolatedWindow = null;
            DataContext = null;
        }
    }
}
