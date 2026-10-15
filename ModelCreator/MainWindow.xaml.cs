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

        private async void ConnectTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectTimer != null && _projectTimer.IsEnabled)
            {
                StatusMessage.Text = "Wait for model setup to finish.";
                return;
            }

            ConnectTestButton.IsEnabled = false;
            try
            {
                string modelName = ModelNaming.ValidateAndBuild(
                    ContractNumberInput.Text, ProjectNameInput.Text);
                string expected = _launcher.GetDestination(modelName);
                StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                StatusMessage.Text = "Opening Tekla's built-in Trimble Connect collaboration...";
                string result = await Task.Run(() => RunConnectMacroHelper(expected));
                StatusMessage.Text = result;
                if (result.StartsWith("OK:", StringComparison.Ordinal))
                {
                    StatusMessage.Foreground = System.Windows.Media.Brushes.DarkGreen;
                }
                else
                {
                    StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                }

                new ConnectDiagnosticsWindow(
                    "Tekla 2026 built-in Trimble Connect collaboration",
                    result + "\n\nExpected Connect project name: " + modelName +
                    "\nRegion: Europe" +
                    "\nLicence: TrimbleConnectBusinessPremium — Severfield plc" +
                    "\n\nThis button opens the Tekla-side workflow through a recorded macro " +
                    "if available. It does not claim the cloud project is created or linked.")
                { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "Trimble Connect: " + ex.Message;
                MessageBox.Show(StatusMessage.Text, "Trimble Connect",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                ConnectTestButton.IsEnabled = true;
            }
        }

        private static string RunConnectMacroHelper(string expectedPath)
        {
            string resultFile = Path.Combine(Path.GetTempPath(),
                "ModelCreatorConnect_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var info = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--open-connect " + QuoteArg(expectedPath) + " " +
                        QuoteArg(resultFile),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                using (Process child = Process.Start(info))
                {
                    if (child == null)
                        throw new InvalidOperationException("Could not start Tekla macro helper.");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(25);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (File.Exists(resultFile))
                        {
                            try
                            {
                                string result = File.ReadAllText(resultFile);
                                if (!string.IsNullOrWhiteSpace(result))
                                    return result;
                            }
                            catch (IOException) { }
                        }
                        if (child.HasExited) break;
                        System.Threading.Thread.Sleep(250);
                    }
                    if (!child.HasExited)
                    {
                        child.Kill();
                        child.WaitForExit(3000);
                    }
                }
                return "ERROR: No response from Tekla macro helper.";
            }
            finally
            {
                try { if (File.Exists(resultFile)) File.Delete(resultFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
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
                    // The helper may have written its result but still be shutting down
                    // its Tekla/Open API runtime. Use the result as completion signal,
                    // rather than waiting for full process exit.
                    DateTime deadline = DateTime.UtcNow.AddSeconds(startSharing ? 120 : 20);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (File.Exists(resultFile))
                        {
                            try
                            {
                                string response = File.ReadAllText(resultFile);
                                if (!string.IsNullOrWhiteSpace(response))
                                    return response;
                            }
                            catch (IOException) { } // Helper may still be writing.
                        }
                        if (child.HasExited)
                            break;
                        System.Threading.Thread.Sleep(250);
                    }
                    if (!child.HasExited)
                    {
                        try { child.Kill(); child.WaitForExit(3000); }
                        catch (InvalidOperationException) { }
                    }
                    return startSharing
                        ? "ERROR\nModel Sharing response not received. Check its status in Tekla; do not retry sharing automatically."
                        : "WAIT\nTekla API helper timed out waiting for a connection.";
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
                        StatusMessage.Text = "Model Sharing complete. Opening Trimble Connect collaboration in Tekla...";
                        string connectOutcome = await Task.Run(
                            () => RunConnectMacroHelper(_expectedModelPath));
                        StatusMessage.Text = "Model created; project properties saved; " +
                            "Model Sharing started. Trimble Connect: " + connectOutcome;
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
