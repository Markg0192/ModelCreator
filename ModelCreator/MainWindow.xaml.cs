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
        private string _lastDiagnostic = "";

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
                {
                    CreateButton.IsEnabled = true;
                    return;
                }

                string license = ((ComboBoxItem)LicenseSelector.SelectedItem).Tag.ToString();
                _launcher.Launch(name, license);

                _expectedModelPath = path;
                _pendingContract = contract;
                _pendingProject = project;
                _lastDiagnostic = "Waiting for the first Tekla API check.";
                _projectDeadline = DateTime.UtcNow.AddMinutes(5);

                _projectTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _projectTimer.Tick += ProjectTimer_Tick;
                _projectTimer.Start();

                StatusMessage.Foreground = System.Windows.Media.Brushes.DarkGreen;
                StatusMessage.Text = "Tekla launched. Waiting for the new model to open...";
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

        private void RetryPropertiesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectTimer != null && _projectTimer.IsEnabled)
            {
                StatusMessage.Text = "Wait for the current operation to finish before retrying.";
                return;
            }

            RetryPropertiesButton.IsEnabled = false;
            try
            {
                string name = ModelNaming.ValidateAndBuild(
                    ContractNumberInput.Text, ProjectNameInput.Text);
                string path = _launcher.GetDestination(name);
                string contract = ContractNumberInput.Text.Trim().ToUpperInvariant();
                string project = ProjectNameInput.Text.Trim();
                string diagnostic;
                string actual = TeklaProjectProperties.GetConnectedModelPath(out diagnostic);
                if (actual == null)
                {
                    StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                    StatusMessage.Text = "Cannot connect to open model: " + diagnostic;
                    MessageBox.Show(StatusMessage.Text, "Tekla connection", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string expected = System.IO.Path.GetFullPath(path)
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                    StatusMessage.Text = "Connected to a different model. Connected: " + actual +
                        " | Expected from inputs: " + expected + ". No changes made.";
                    MessageBox.Show(StatusMessage.Text, "Model mismatch", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string result;
                bool success = TeklaProjectProperties.TryApply(path, contract, project, out result);
                StatusMessage.Foreground = success
                    ? System.Windows.Media.Brushes.DarkGreen
                    : System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = success
                    ? "Project properties updated successfully: " + contract + " - " + project
                    : "Project properties not updated: " + result;
                MessageBox.Show(StatusMessage.Text, "Project properties", MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "Project property retry error: " + ex.Message;
                MessageBox.Show(StatusMessage.Text, "Project properties", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                RetryPropertiesButton.IsEnabled = true;
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
                StatusMessage.Text = "Project properties were not confirmed within five minutes. " +
                    "Last check: " + _lastDiagnostic;
                return;
            }

            try
            {
                string diagnostic;
                if (!TeklaProjectProperties.TryApply(
                        _expectedModelPath, _pendingContract, _pendingProject, out diagnostic))
                {
                    _lastDiagnostic = diagnostic;
                    StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                    StatusMessage.Text = diagnostic;
                    return;
                }

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
