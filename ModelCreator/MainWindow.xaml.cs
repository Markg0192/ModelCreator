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
                    return;

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

        private void TestProjectPropertiesButton_Click(object sender, RoutedEventArgs e)
        {
            TestProjectPropertiesButton.IsEnabled = false;
            try
            {
                // Reuse existing input validation but do not create or launch a model.
                ModelNaming.ValidateAndBuild(
                    ContractNumberInput.Text, ProjectNameInput.Text);
                string contract = ContractNumberInput.Text.Trim().ToUpperInvariant();
                string project = ProjectNameInput.Text.Trim();

                string diagnostic;
                string connectedPath = TeklaProjectProperties.GetConnectedModelPath(out diagnostic);
                if (connectedPath == null)
                {
                    StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                    StatusMessage.Text = "Test failed: " + diagnostic;
                    MessageBox.Show(StatusMessage.Text, "Tekla connection test",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (MessageBox.Show(
                    "Connected to this Tekla model:\\n\\n" + connectedPath +
                    "\\n\\nSet Project Number to: " + contract +
                    "\\nSet Project Name to: " + project +
                    "\\n\\nThis modifies the OPEN model. Continue?",
                    "Confirm project property test",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    StatusMessage.Text = "Test cancelled; no project properties changed.";
                    return;
                }

                string writeDiagnostic;
                bool success = TeklaProjectProperties.TryApply(
                    connectedPath, contract, project, out writeDiagnostic);

                StatusMessage.Foreground = success
                    ? System.Windows.Media.Brushes.DarkGreen
                    : System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = success
                    ? "Test passed. Project properties updated in: " + connectedPath
                    : "Test did not update the model: " + writeDiagnostic;
                MessageBox.Show(StatusMessage.Text, "Tekla project properties test",
                    MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "Test error: " + ex.ToString();
                MessageBox.Show(StatusMessage.Text, "Tekla project properties test",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                TestProjectPropertiesButton.IsEnabled = true;
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
