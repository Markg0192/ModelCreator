using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

namespace ModelCreator.Services
{
    // UIA discovery only. Never drives or submits cloud-creation controls.
    internal static class TeklaWindowInspector
    {
        internal static string Inspect(string expectedModelName)
        {
            var lines = new List<string>();
            var processes = Process.GetProcessesByName("TeklaStructures");
            var pids = new HashSet<int>(processes.Select(p => p.Id));
            lines.Add("Focused Tekla / Trimble Connect control inspection (read-only)");
            lines.Add("Expected model: " + expectedModelName);
            lines.Add("Tekla processes: " + processes.Length);
            try
            {
                AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement window in windows)
                {
                    try
                    {
                        int pid = window.Current.ProcessId;
                        string title = window.Current.Name ?? "";
                        bool isTekla = pids.Contains(pid);
                        // Only inspect windows belonging to Tekla. Matching a title
                        // alone also picks up Visual Studio and web browser tabs.
                        if (!isTekla) continue;
                        lines.Add("Window PID=" + pid + " TeklaProcess=" + isTekla +
                            " title='" + Clip(title) + "'");
                        int visited = 0, reported = 0;
                        Walk(window, lines, 0, ref visited, ref reported);
                        lines.Add("  Visited controls: " + visited +
                            "; matching controls: " + reported);
                    }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                }
            }
            catch (Exception ex)
            {
                lines.Add("UI Automation failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            // A collaboration dialog may be hosted by another process (WebView or
            // companion executable). Inventory visible desktop windows so that
            // we can identify its actual owner without dumping unrelated controls.
            lines.Add("Visible desktop window inventory (title, PID, executable only):");
            try
            {
                AutomationElementCollection allWindows = AutomationElement.RootElement.FindAll(
                    TreeScope.Children, Condition.TrueCondition);
                int shown = 0;
                foreach (AutomationElement candidate in allWindows)
                {
                    if (shown >= 60) break;
                    try
                    {
                        string title = candidate.Current.Name ?? "";
                        if (string.IsNullOrWhiteSpace(title) ||
                            candidate.Current.IsOffscreen) continue;
                        int id = candidate.Current.ProcessId;
                        string exe;
                        try
                        {
                            using (Process proc = Process.GetProcessById(id))
                                exe = proc.ProcessName;
                        }
                        catch (ArgumentException) { exe = "(exited)"; }
                        bool relevant = Contains(title, "collaboration") ||
                            Contains(title, "trimble") || Contains(title, "connect") ||
                            Contains(title, "project") || Contains(exe, "tekla") ||
                            Contains(exe, "trimble");
                        if (!relevant) continue;
                        lines.Add("  PID=" + id + " process=" + exe +
                            " title='" + Clip(title) + "'");
                        shown++;
                        // Expand only likely collaboration windows from a process
                        // other than the main Tekla process.
                        if (!pids.Contains(id) &&
                            (Contains(title, "collaboration") ||
                             Contains(title, "trimble connect") ||
                             Contains(title, "new project")))
                        {
                            int seen = 0, matches = 0;
                            Walk(candidate, lines, 0, ref seen, ref matches);
                            lines.Add("  Controls visited=" + seen +
                                " matching=" + matches);
                        }
                    }
                    catch (ElementNotAvailableException) { }
                    catch (InvalidOperationException) { }
                }
            }
            catch (Exception ex)
            {
                lines.Add("Desktop inventory unavailable: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
            lines.Add("Tip: Keep the Start collaboration dialog visible in Tekla " +
                "during inspection; if no dialog appears, open the File menu or " +
                "Trimble Connect ribbon and inspect again.");
            lines.Add("No controls activated; no project created.");
            return string.Join(Environment.NewLine, lines);
        }

        private static void Walk(AutomationElement parent, List<string> output, int depth,
            ref int visited, ref int reported)
        {
            if (depth >= 12 || visited >= 4000 || reported >= 120) return;
            AutomationElementCollection children;
            try { children = parent.FindAll(TreeScope.Children, Condition.TrueCondition); }
            catch (ElementNotAvailableException) { return; }
            catch (InvalidOperationException) { return; }

            foreach (AutomationElement child in children)
            {
                if (visited >= 4000 || reported >= 120) break;
                visited++;
                try
                {
                    string name = child.Current.Name ?? "";
                    string id = child.Current.AutomationId ?? "";
                    string type = child.Current.ControlType.ProgrammaticName;
                    bool relevant = Contains(name, "trimble") || Contains(name, "connect") ||
                        Contains(name, "collaboration") || Contains(name, "project") ||
                        Contains(name, "licence") || Contains(name, "license") ||
                        Contains(name, "europe") || Contains(id, "trimble") ||
                        Contains(id, "connect") || Contains(id, "collaboration") ||
                        Contains(id, "AID_MenuButton");
                    if (relevant)
                    {
                        output.Add("  depth=" + depth + " " + type + " name='" +
                            Clip(name) + "' id='" + Clip(id) + "'");
                        reported++;
                    }
                    Walk(child, output, depth + 1, ref visited, ref reported);
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
            }
        }

        private static bool Contains(string text, string search)
        {
            return text != null && text.IndexOf(search,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Clip(string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            return text.Length > 130 ? text.Substring(0, 130) + "..." : text;
        }
    }
}
