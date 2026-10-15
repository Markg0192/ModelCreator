using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Automation;

namespace ModelCreator.Services
{
    // Read-only UI Automation inspection. No coordinate clicking, keyboard injection,
    // or interactions with unrelated windows.
    internal static class TeklaWindowInspector
    {
        internal static string Inspect(string expectedModelName)
        {
            var lines = new List<string>();
            var processes = Process.GetProcessesByName("TeklaStructures");
            lines.Add("Tekla UI inspection (read-only)");
            lines.Add("Expected model: " + expectedModelName);
            lines.Add("Tekla processes found: " + processes.Length);
            foreach (Process process in processes.Take(5))
            {
                lines.Add("Process PID " + process.Id);
                try
                {
                    AutomationElement window = AutomationElement.FromHandle(process.MainWindowHandle);
                    if (window == null)
                    {
                        lines.Add("  No accessible main window (Tekla may still be starting).");
                        continue;
                    }
                    lines.Add("  Window: " + SafeName(window));
                    var count = 0;
                    Walk(window, lines, 0, ref count);
                }
                catch (Exception ex)
                {
                    lines.Add("  UI Automation unavailable: " + ex.GetType().Name +
                        ": " + ex.Message);
                }
            }
            if (processes.Length == 0)
                lines.Add("Tekla is not running. Open the test model first.");
            return string.Join(Environment.NewLine, lines);
        }

        private static void Walk(AutomationElement parent, List<string> output,
            int depth, ref int count)
        {
            if (depth >= 5 || count >= 180) return;
            AutomationElementCollection children;
            try
            {
                children = parent.FindAll(TreeScope.Children, Condition.TrueCondition);
            }
            catch (ElementNotAvailableException) { return; }
            foreach (AutomationElement child in children)
            {
                if (count >= 180) break;
                count++;
                try
                {
                    string name = SafeName(child);
                    string automationId = child.Current.AutomationId;
                    string type = child.Current.ControlType.ProgrammaticName;
                    output.Add(new string(' ', (depth + 1) * 2) +
                        type + " name='" + Clip(name) + "' id='" + Clip(automationId) + "'");
                    Walk(child, output, depth + 1, ref count);
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
            }
        }

        private static string SafeName(AutomationElement element)
        {
            return element.Current.Name ?? "";
        }

        private static string Clip(string value)
        {
            value = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            return value.Length > 110 ? value.Substring(0, 110) + "..." : value;
        }
    }
}
