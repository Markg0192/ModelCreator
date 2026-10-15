using System.Windows;

namespace ModelCreator
{
    public partial class ConnectDiagnosticsWindow : Window
    {
        public ConnectDiagnosticsWindow(string summary, string details)
        {
            InitializeComponent();
            SummaryText.Text = summary;
            DetailsText.Text = details;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(SummaryText.Text + System.Environment.NewLine +
                System.Environment.NewLine + DetailsText.Text);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
