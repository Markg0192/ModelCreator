using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

namespace ModelCreator.Services
{
    // Invoke Tekla's own Trimble Connector only with exact verified selections.
    // Never launch an ambiguous POST or auto-retry an attempted Create action.
    internal static class TrimbleConnectorAutomation
    {
        private const string ProjectId = "AID_TrimbleConnector.AttachProject.Projects";
        private const string RegionId = "AID_TrimbleConnector.AttachProject.Regions";
        private const string LicenseId = "AID_TrimbleConnector.AttachProject.Licenses";

        internal static string InspectSelection(string expectedName)
        {
            AutomationElement root = FindDialog();
            if (root == null)
                return "WAIT: Trimble Connector Select project dialog is not visible.";
            AutomationElement project = Find(root, ProjectId);
            AutomationElement region = Find(root, RegionId);
            AutomationElement license = Find(root, LicenseId);
            AutomationElement create = Find(root, "okButton");
            string projectValue = GetProjectValue(project);
            string regionValue = SelectedText(region);
            string licenseValue = SelectedText(license);
            bool validProject = string.Equals(projectValue, expectedName, StringComparison.OrdinalIgnoreCase);
            bool validRegion = string.Equals(regionValue, "Europe", StringComparison.OrdinalIgnoreCase);
            bool validLicense = licenseValue.IndexOf("Severfield plc", StringComparison.OrdinalIgnoreCase) >= 0 &&
                licenseValue.IndexOf("TrimbleConnectBusinessPremium", StringComparison.OrdinalIgnoreCase) >= 0;
            bool enabled = create != null && create.Current.IsEnabled;
            string report = "Project: " + projectValue + "\nRegion: " + regionValue +
                "\nLicence: " + licenseValue + "\nCreate enabled: " + enabled +
                "\nProject matches: " + validProject + "\nEurope matches: " + validRegion +
                "\nSeverfield Business Premium matches: " + validLicense;
            return validProject && validRegion && validLicense && enabled
                ? "READY\n" + report : "CHECK\n" + report +
                    "\nNothing was created. Select the expected project, Europe, " +
                    "and Severfield Business Premium in Tekla's dialog.";
        }

        internal static string SelectEuropeAndCreate(string expectedName)
        {
            AutomationElement dialog = FindDialog();
            if (dialog == null) return "BLOCKED\nConnector dialog not found.";
            AutomationElement region = Find(dialog, RegionId);
            if (region == null) return "BLOCKED\nRegion selector not found.";

            if (!string.Equals(SelectedText(region), "Europe",
                StringComparison.OrdinalIgnoreCase))
            {
                object pattern;
                if (!region.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern))
                    return "BLOCKED\nRegion selector cannot be expanded.";
                ((ExpandCollapsePattern)pattern).Expand();

                AutomationElement europe = null;
                DateTime deadline = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < deadline && europe == null)
                {
                    // WPF ComboBox items may appear in a separate popup window.
                    foreach (AutomationElement window in AutomationElement.RootElement.FindAll(
                        TreeScope.Children, Condition.TrueCondition))
                    {
                        if (window.Current.ProcessId != dialog.Current.ProcessId) continue;
                        var items = window.FindAll(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.NameProperty,
                                "Europe", PropertyConditionFlags.IgnoreCase));
                        foreach (AutomationElement item in items)
                        {
                            if (item.Current.ControlType == ControlType.ListItem &&
                                item.Current.IsEnabled && !item.Current.IsOffscreen)
                            { europe = item; break; }
                        }
                        if (europe != null) break;
                    }
                    if (europe == null) System.Threading.Thread.Sleep(100);
                }

                if (europe == null)
                    return "BLOCKED\nEurope option not found in region dropdown.";
                if (!europe.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                    return "BLOCKED\nEurope option cannot be selected.";
                ((SelectionItemPattern)pattern).Select();
                System.Threading.Thread.Sleep(250);
            }

            // Region changes may refresh the licence list, so always recheck.
            return CreateIfVerified(expectedName);
        }

        internal static string CreateIfVerified(string expectedName)
        {
            string status = InspectSelection(expectedName);
            if (!status.StartsWith("READY\n", StringComparison.Ordinal))
                return "BLOCKED\n" + status;
            AutomationElement dialog = FindDialog();
            if (dialog == null) return "BLOCKED\nDialog was closed.";
            AutomationElement button = Find(dialog, "okButton");
            if (button == null || !button.Current.IsEnabled)
                return "BLOCKED\nCreate button no longer available.";
            object pattern;
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                return "BLOCKED\nCreate button does not expose InvokePattern.";
            ((InvokePattern)pattern).Invoke();
            return "SUBMITTED\nTrimble Connector Create was pressed exactly once. " +
                "Check Tekla/Trimble Connect to confirm the project was created and linked. " +
                "Do not press Create again until its status is confirmed.";
        }

        private static AutomationElement FindDialog()
        {
            var pids = Process.GetProcessesByName("TrimbleConnector")
                .Select(p => p.Id).ToArray();
            foreach (AutomationElement window in AutomationElement.RootElement.FindAll(
                TreeScope.Children, Condition.TrueCondition))
            {
                try
                {
                    if (!pids.Contains(window.Current.ProcessId) ||
                        window.Current.IsOffscreen) continue;
                    if (Find(window, ProjectId) != null &&
                        Find(window, RegionId) != null &&
                        Find(window, LicenseId) != null) return window;
                }
                catch (ElementNotAvailableException) { }
            }
            return null;
        }

        private static AutomationElement Find(AutomationElement root, string id)
        {
            return root == null ? null : root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        }

        private static string GetProjectValue(AutomationElement combo)
        {
            if (combo == null) return "(missing project field)";
            var edit = combo.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit != null)
            {
                object p;
                if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out p))
                    return ((ValuePattern)p).Current.Value.Trim();
            }
            return SelectedText(combo);
        }

        private static string SelectedText(AutomationElement combo)
        {
            if (combo == null) return "(control not found)";
            object pattern;
            if (combo.TryGetCurrentPattern(SelectionPattern.Pattern, out pattern))
            {
                var selection = ((SelectionPattern)pattern).Current.GetSelection();
                if (selection.Length > 0)
                    return selection[0].Current.Name ?? "";
            }
            if (combo.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                return ((ValuePattern)pattern).Current.Value ?? "";
            // WPF combo box may only expose the selected value through a text child.
            var texts = combo.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            return string.Join(" ", texts.Cast<AutomationElement>()
                .Select(x => x.Current.Name).Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }
}
