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
        private bool _connectAttempted;
        private bool _connectCompleted;

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
                _connectAttempted = false;
                _connectCompleted = false;
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

        private async void CreateFoldersButton_Click(object sender, RoutedEventArgs e)
        {
            if (_projectTimer != null && _projectTimer.IsEnabled)
            {
                StatusMessage.Text = "Wait until model setup finishes.";
                return;
            }
            CreateFoldersButton.IsEnabled = false;
            try
            {
                string modelName = ModelNaming.ValidateAndBuild(
                    ContractNumberInput.Text, ProjectNameInput.Text);
                string diagnostic;
                string connected = TeklaProjectProperties.GetConnectedModelPath(out diagnostic);
                string expected = Path.GetFullPath(_launcher.GetDestination(modelName))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (connected == null ||
                    !string.Equals(connected, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Wrong Tekla model is open. " +
                        (connected ?? diagnostic) + ". Nothing created.");

                if (MessageBox.Show("Create root-level folders 'test 1' and 'test 2' " +
                    "in the existing Europe Trimble Connect project '" + modelName +
                    "'?\n\nThis will not create another Connect project. " +
                    "The project and Severfield Business Premium ownership will be " +
                    "verified first. Do not repeat after an uncertain result.",
                    "Confirm test folder creation", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;

                StatusMessage.Text = "Signing in to Trimble Connect to add test folders...";
                string token = await TrimbleIdentity.LoginAsync();
                string result = await TrimbleConnectProjects.AddTestFoldersToExistingProjectAsync(
                    token, modelName);
                StatusMessage.Text = "Trimble Connect folder test finished. See result window.";
                new ConnectDiagnosticsWindow("Trimble Connect folder setup", result)
                { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = "Folder setup: " + ex.Message;
                MessageBox.Show(StatusMessage.Text, "Trimble Connect folders",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { CreateFoldersButton.IsEnabled = true; }
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
                string expected = Path.GetFullPath(_launcher.GetDestination(modelName))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string diag;
                string actual = TeklaProjectProperties.GetConnectedModelPath(out diag);
                if (actual == null || !string.Equals(actual, expected,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Open Tekla model does not match " +
                        modelName + ". " + (actual ?? diag) + ". Nothing created.");

                MessageBoxResult approval = MessageBox.Show(
                    "Create and link the Trimble Connect project '" + modelName +
                    "' using Tekla's existing Connector dialog?\n\n" +
                    "Only proceed if this model has no Connect project linked. " +
                    "ModelCreator will minimise and try to open File > Trimble Connect > " +
                    "Start collaboration. If it cannot open the dialog, do so manually. " +
                    "Select the new project name, Europe, and " +
                    "Severfield plc (TrimbleConnectBusinessPremium).\n\n" +
                    "The application will check the selections and press Create ONCE " +
                    "if they match. A failed or unclear result will not be retried.",
                    "Confirm Trimble Connect project creation",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (approval != MessageBoxResult.Yes) return;

                StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                StatusMessage.Text = "Opening Tekla File > Trimble Connect > Start collaboration...";
                WindowState = WindowState.Minimized;
                string menuResult = await Task.Run(() =>
                    TeklaConnectMenuAutomation.OpenCollaboration(modelName));
                string result = await Task.Run(() =>
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                    string last = "WAIT: Dialog not found.";
                    DateTime? firstVisible = null;
                    int readyChecks = 0;
                    while (DateTime.UtcNow < deadline)
                    {
                        try
                        {
                            last = TrimbleConnectorAutomation.InspectSelection(modelName);
                            if (last.StartsWith("WAIT:", StringComparison.Ordinal))
                            {
                                firstVisible = null;
                                readyChecks = 0;
                            }
                            else
                            {
                                if (!firstVisible.HasValue)
                                    firstVisible = DateTime.UtcNow;
                                // Let the external Connector populate its dropdowns.
                                // Two stable positive readings and a 1.5s settling
                                // period are required before invoking Create once.
                                if (last.StartsWith("READY\n", StringComparison.Ordinal))
                                    readyChecks++;
                                else
                                    readyChecks = 0;
                                if (readyChecks >= 2 &&
                                    DateTime.UtcNow - firstVisible.Value >=
                                        TimeSpan.FromMilliseconds(1500))
                                    return TrimbleConnectorAutomation.CreateIfVerified(modelName);
                            }
                        }
                        catch (System.Windows.Automation.ElementNotEnabledException)
                        {
                            // The Connect window is still enabling its controls.
                            readyChecks = 0;
                        }
                        catch (System.Windows.Automation.ElementNotAvailableException)
                        {
                            readyChecks = 0;
                            firstVisible = null;
                        }
                        catch (Exception ex)
                        {
                            return "BLOCKED\n" + ex.GetType().Name + ": " + ex.Message;
                        }
                        System.Threading.Thread.Sleep(300);
                    }
                    return "BLOCKED\n" + menuResult + "\n" + last +
                        "\nThe Connector did not settle on the required settings. " +
                        "Nothing was created.";
                });
                WindowState = WindowState.Normal;
                Activate();
                StatusMessage.Text = result.StartsWith("SUBMITTED\n",
                    StringComparison.Ordinal)
                    ? "Trimble Connect creation submitted. Verify linking inside Tekla."
                    : "Trimble Connect creation blocked; read diagnostics.";
                new ConnectDiagnosticsWindow("Trimble Connector project creation", result)
                { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                WindowState = WindowState.Normal;
                StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                StatusMessage.Text = ex.Message;
                MessageBox.Show(ex.Message, "Trimble Connect", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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

        private async Task<string> RunConnectCreationAndFoldersAsync(string name)
        {
            WindowState = WindowState.Minimized;
            try
            {
                string menu = await Task.Run(() =>
                    TeklaConnectMenuAutomation.OpenCollaboration(name));
                string submitted = await Task.Run(() =>
                {
                    DateTime until = DateTime.UtcNow.AddSeconds(45);
                    DateTime? firstVisible = null;
                    int stable = 0;
                    string last = "WAIT: Connector not visible.";
                    while (DateTime.UtcNow < until)
                    {
                        try
                        {
                            last = TrimbleConnectorAutomation.InspectSelection(name);
                            if (last.StartsWith("WAIT:", StringComparison.Ordinal))
                            {
                                stable = 0;
                                firstVisible = null;
                            }
                            else
                            {
                                if (!firstVisible.HasValue) firstVisible = DateTime.UtcNow;
                                stable = last.StartsWith("READY\n", StringComparison.Ordinal)
                                    ? stable + 1 : 0;
                                if (stable >= 2 && DateTime.UtcNow - firstVisible.Value >=
                                    TimeSpan.FromMilliseconds(1500))
                                    return TrimbleConnectorAutomation.CreateIfVerified(name);
                            }
                        }
                        catch (System.Windows.Automation.ElementNotAvailableException)
                        { stable = 0; firstVisible = null; }
                        catch (System.Windows.Automation.ElementNotEnabledException)
                        { stable = 0; }
                        System.Threading.Thread.Sleep(300);
                    }
                    return "BLOCKED\n" + menu + "\n" + last;
                });
                if (!submitted.StartsWith("SUBMITTED\n", StringComparison.Ordinal))
                    return "CONNECT BLOCKED: " + submitted +
                        "\nNo Model Sharing attempt made.";
            }
            finally
            {
                WindowState = WindowState.Normal;
            }

            // A submitted dialog is not confirmation. Wait for the actual project
            // to become available before attempting folders or Model Sharing.
            StatusMessage.Text = "Trimble Connect creation submitted. " +
                "Waiting for the new cloud project...";
            string token = await TrimbleIdentity.LoginAsync();
            string lastResult = "";
            for (int attempt = 0; attempt < 6; attempt++)
            {
                // Only retry read/lookup when the new project isn't visible yet.
                // The folder write itself is NEVER retried.
                string lookup = await TrimbleConnectProjects.PreflightAsync(token, name);
                if (lookup.IndexOf("already uses the name", StringComparison.Ordinal) >= 0)
                    break;
                if (attempt == 5)
                    return "CONNECT UNCERTAIN: New cloud project is not visible yet. " +
                        "No folders added and Model Sharing not started.";
                await Task.Delay(2000);
            }
            lastResult = await TrimbleConnectProjects.AddTestFoldersToExistingProjectAsync(
                token, name);
            if (!lastResult.Contains("test 1: POST accepted") ||
                !lastResult.Contains("test 2: POST accepted"))
                return "FOLDER SETUP NOT CONFIRMED: " + lastResult +
                    "\nCheck Connect before retrying. Model Sharing not started.";
            return "OK\n" + lastResult;
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
                if (_projectPropertiesSaved && !_connectCompleted)
                {
                    if (_connectAttempted) return;
                    _connectAttempted = true;
                    StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                    StatusMessage.Text = "Stage 3/5: Creating Trimble Connect project and folders...";
                    string connect = await RunConnectCreationAndFoldersAsync(
                        ModelNaming.ValidateAndBuild(_pendingContract, _pendingProject));
                    if (!connect.StartsWith("OK\n", StringComparison.Ordinal))
                    {
                        StopProjectTimer();
                        StatusMessage.Foreground = System.Windows.Media.Brushes.Firebrick;
                        StatusMessage.Text = connect;
                        new ConnectDiagnosticsWindow("ModelCreator setup stopped", connect)
                        { Owner = this }.ShowDialog();
                        return;
                    }
                    _connectCompleted = true;
                    StatusMessage.Text = "Stages 3/4 complete: Connect project and folders created. " +
                        "Starting Model Sharing...";
                    return;
                }
                bool startSharing = _projectPropertiesSaved && _connectCompleted;
                if (startSharing)
                {
                    // Start cloud sharing ONCE. A lost result is ambiguous and must not
                    // trigger another automatic cloud creation attempt.
                    if (_sharingAttempted)
                        return;
                    _sharingAttempted = true;
                    StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                    StatusMessage.Text = "Stage 5/5: Starting Tekla Model Sharing...";
                }
                else
                {
                    StatusMessage.Foreground = System.Windows.Media.Brushes.DarkOrange;
                    StatusMessage.Text = "Stage 2/5: Connecting and saving project properties...";
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
                        StatusMessage.Text = "Stage 2/5 complete: Project properties saved. Starting Trimble Connect...";
                    }
                    else
                    {
                        _sharingStarted = true;
                        StopProjectTimer();
                        StatusMessage.Foreground = System.Windows.Media.Brushes.DarkGreen;
                        StatusMessage.Text = "Model created, properties saved, Connect project and test folders created, " +
                            "and Tekla Model Sharing started.";
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
