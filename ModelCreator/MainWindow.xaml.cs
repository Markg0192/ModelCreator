using System;
using System.Windows;
using System.Windows.Controls;
using ModelCreator.Services;

namespace ModelCreator
{
    public partial class MainWindow : Window
    {
        private readonly TeklaModelLauncher _launcher = new TeklaModelLauncher();

        public MainWindow()
        {
            InitializeComponent();
            UpdatePreview();
        }

        private void InputsChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (ModelNamePreview == null || DestinationPreview == null)
                return;

            string name = ModelNaming.BuildPreview(
                ContractNumberInput.Text, ProjectNameInput.Text);

            ModelNamePreview.Text = name;
            DestinationPreview.Text = _launcher.GetDestination(name);
            StatusMessage.Text = "";
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            CreateButton.IsEnabled = false;
            try
            {
                string name = ModelNaming.ValidateAndBuild(
                    ContractNumberInput.Text, ProjectNameInput.Text);

                string path = _launcher.GetDestination(name);
                if (MessageBox.Show(
                    "Create the following Tekla Structures 2026 model?\n\n" +
                    name + "\n\n" + path + "\n\nLicence: " +
                    ((ComboBoxItem)LicenseSelector.SelectedItem).Content,
                    "Confirm model creation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                string license = ((ComboBoxItem)LicenseSelector.SelectedItem).Tag.ToString();
                _launcher.Launch(name, license);
                StatusMessage.Foreground =
                    System.Windows.Media.Brushes.DarkGreen;
                StatusMessage.Text =
                    "Tekla has been launched with the model creation command. " +
                    "Confirm the model opens successfully before starting another.";
            }
            catch (Exception ex)
            {
                StatusMessage.Foreground =
                    System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = ex.Message;
                MessageBox.Show(ex.Message, "ModelCreator",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                CreateButton.IsEnabled = true;
            }
        }
    }
}
