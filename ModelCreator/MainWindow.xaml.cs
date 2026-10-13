using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ModelCreator.Services;

namespace ModelCreator
{
    public partial class MainWindow : Window
    {
        private readonly TeklaModelLauncher _launcher = new TeklaModelLauncher();
        private DispatcherTimer _projectTimer;
        private DateTime _projectDeadline;
        private string _expectedModelPath;
        private string _pendingContract;
        private string _pendingProject;

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
            if (_projectTimer == null || !_projectTimer.IsEnabled)
                StatusMessage.Text = "";
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectTimer != null && _projectTimer.IsEnabled)
                return;

            CreateButton.IsEnabled = false;
            try
            {
                string name = ModelNaming.ValidateAndBuild(
                    ContractNumberInput.Text, ProjectNameInput.Text);
                string contract = ContractNumberInput.Text.Trim().ToUpperInvariant();
                string project = ProjectNameInput.Text.Trim();
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

                _expectedModelPath = path;
                _pendingContract = contract;
                _pendingProject = project;
                _projectDeadline = DateTime.UtcNow.AddMinutes(5);

                _projectTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _projectTimer.Tick += ProjectTimer_Tick;
                _projectTimer.Start();

                StatusMessage.Foreground = System.Windows.Media.Brushes.DarkGreen;
                StatusMessage.Text = "Tekla launched. Waiting for the new model to open " +
                                     "before setting project properties...";
            }
            catch (Exception ex)
            {
                CreateButton.IsEnabled = true;
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = ex.Message;
                MessageBox.Show(ex.Message, "ModelCreator",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StopProjectTimer()
        {
            if (_projectTimer != null)
            {
                _projectTimer.Stop();
                _projectTimer.Tick -= ProjectTimer_Tick;
                _projectTimer = null;
            }
            CreateButton.IsEnabled = true;
        }

        private void ProjectTimer_Tick(object sender, EventArgs e)
        {
            if (DateTime.UtcNow >= _projectDeadline)
            {
                StopProjectTimer();
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "The model launch was requested, but project properties " +
                    "could not be confirmed within five minutes. Check the model in Tekla.";
                return;
            }

            try
            {
                if (!TeklaProjectProperties.TryApply(
                        _expectedModelPath, _pendingContract, _pendingProject))
                    return;

                StopProjectTimer();
                StatusMessage.Foreground = System.Windows.Media.Brushes.DarkGreen;
                StatusMessage.Text = "Project properties updated successfully: " +
                    _pendingContract + " - " + _pendingProject;
            }
            catch (Exception ex)
            {
                StopProjectTimer();
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "Model launched, but project properties could not be updated: " +
                                     ex.Message;
                MessageBox.Show(StatusMessage.Text, "ModelCreator",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
