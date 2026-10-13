using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
        private bool _projectProbeRunning;
        private bool _projectPropertiesSaved;
        private bool _sharingStarted;
        private bool _sharingAttempted;

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
                _projectPropertiesSaved = false;
                _sharingStarted = false;
                _sharingAttempted = false;
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

        private void ConnectTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectTimer != null && _projectTimer.IsEnabled)
            {
                StatusMessage.Text = "Finish model setup before connecting Trimble Connect.";
                return;
            }

            try
            {
                string name = ModelNaming.ValidateAndBuild(
                    ContractNumberInput.Text, ProjectNameInput.Text);
                string expected = Path.GetFullPath(_launcher.GetDestination(name))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string diagnostic;
                string connected = TeklaProjectProperties.GetConnectedModelPath(out diagnostic);
                if (connected == null)
                    throw new InvalidOperationException(diagnostic);
                if (!string.Equals(expected, connected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Wrong model open. Connected: " + connected +
                        " | Expected: " + expected);

                string instructions = "Correct Tekla model confirmed:\n\n" + connected +
                    "\n\nTo create AND link its Trimble Connect project, in Tekla choose:" +
                    "\nFile > Trimble Connect > Start collaboration" +
                    "\n\nEnter the new project name: " + name +
                    "\nChoose your company's approved project server location and licence." +
                    "\n\nConfirm creation in Tekla. This test button does not yet create " +
                    "a Connect project automatically; API authentication and the correct " +
                    "company subscription must be configured before we automate it.";
                MessageBox.Show(instructions, "Trimble Connect assisted test",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                StatusMessage.Text = "Trimble Connect: model verified. Complete project creation " +
                    "and linking in Tekla via File > Trimble Connect > Start collaboration.";
            }
            catch (Exception ex)
            {
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "Trimble Connect test: " + ex.Message;
                MessageBox.Show(StatusMessage.Text, "Trimble Connect",
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

        private static string ProbeProjectProperties(string expectedPath, string contract,
            string project, bool startSharing)
        {
            string resultFile = Path.Combine(Path.GetTempPath(),
                "ModelCreatorProbe_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var info = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = (startSharing ? "--start-sharing " : "--apply-project-properties ") +
                        QuoteArg(expectedPath) + " " +
                        QuoteArg(contract) + " " + QuoteArg(project) + " " + QuoteArg(resultFile),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                using (Process child = Process.Start(info))
                {
                    if (child == null)
                        throw new InvalidOperationException("Could not launch Tekla API helper.");
                    if (!child.WaitForExit(300000))
                    {
                        child.Kill();
                        child.WaitForExit();
                        return "ERROR\nModel setup helper timed out; check Model Sharing before retrying to avoid duplicates.";
                    }
                }
                return File.Exists(resultFile)
                    ? File.ReadAllText(resultFile)
                    : "ERROR\nTekla API helper exited without reporting a result.";
            }
            finally
            {
                try { if (File.Exists(resultFile)) File.Delete(resultFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string QuoteArg(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private async void ProjectTimer_Tick(object sender, EventArgs e)
        {
            if (_projectProbeRunning)
                return;

            if (DateTime.UtcNow >= _projectDeadline)
            {
                StopProjectTimer();
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "Model setup did not finish within five minutes. " +
                    "Last check: " + _lastDiagnostic;
                return;
            }

            _projectProbeRunning = true;
            try
            {
                // Each attempt runs in a new process so early failed Tekla API
                // connections cannot be cached across subsequent attempts.
                bool startSharing = _projectPropertiesSaved;
                if (startSharing)
                {
                    // Start cloud sharing ONCE. A lost result is ambiguous and must not
                    // trigger another automatic cloud creation attempt.
                    if (_sharingAttempted)
                        return;
                    _sharingAttempted = true;
                    StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                    StatusMessage.Text = "Stage 3/3: Starting Tekla Model Sharing...";
                }
                else
                {
                    StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                    StatusMessage.Text = "Stage 2/3: Connecting and saving project properties...";
                }
                string result = await Task.Run(() => ProbeProjectProperties(
                    _expectedModelPath, _pendingContract, _pendingProject, startSharing));
                string[] lines = result.Split(new[] { '\n' }, 2);
                string outcome = lines[0].Trim();
                string diagnostic = lines.Length > 1 ? lines[1].Trim() : result;
                _lastDiagnostic = diagnostic;

                if (outcome == "OK")
                {
                    if (!startSharing)
                    {
                        _projectPropertiesSaved = true;
                        StatusMessage.Foreground = System.Windows.Media.Brushes.DarkGreen;
                        StatusMessage.Text = "Stage 2/3 complete: Project properties saved. " +
                            "Starting Model Sharing next...";
                    }
                    else
                    {
                        _sharingStarted = true;
                        StopProjectTimer();
                        StatusMessage.Foreground = System.Windows.Media.Brushes.DarkGreen;
                        StatusMessage.Text = "Model setup complete: project properties saved and " + diagnostic;
                    }
                }
                else if (outcome == "ERROR")
                {
                    StopProjectTimer();
                    StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                    StatusMessage.Text = (startSharing ? "Stage 3 (Model Sharing) failed: " :
                        "Stage 2 (project properties) failed: ") + diagnostic +
                        (startSharing ? " Check sharing status in Tekla before retrying." : "");
                    MessageBox.Show(StatusMessage.Text, "ModelCreator", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    if (startSharing)
                    {
                        StopProjectTimer();
                        StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                        StatusMessage.Text = "Model Sharing status uncertain. Check in Tekla before retrying: " + diagnostic;
                    }
                    else
                    {
                        StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                        StatusMessage.Text = "Stage 2/3 waiting for Tekla API: " + diagnostic;
                    }
                }
            }
            catch (Exception ex)
            {
                StopProjectTimer();
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "Project connection attempt failed: " + ex.Message;
                MessageBox.Show(StatusMessage.Text, "ModelCreator", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _projectProbeRunning = false;
            }
        }
    }
}
