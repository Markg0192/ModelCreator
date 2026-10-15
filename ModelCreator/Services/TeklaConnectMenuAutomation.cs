using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

namespace ModelCreator.Services
{
    internal static class TeklaConnectMenuAutomation
    {
        // The File menu button is identified from the Tekla 2026 UIA inspection.
        // We deliberately do not click based on screen coordinates.
        internal static string OpenCollaboration(string expectedModelName)
        {
            var processes = Process.GetProcessesByName("TeklaStructures");
            foreach (var process in processes)
            {
                if (process.MainWindowHandle == IntPtr.Zero) continue;
                AutomationElement root = AutomationElement.FromHandle(process.MainWindowHandle);
                if (root == null) continue;
                string title = root.Current.Name ?? "";
                if (title.IndexOf(expectedModelName, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var menu = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "AID_MenuButton"));
                if (menu == null) return "BLOCKED: Tekla File menu button was not found.";
                if (!Invoke(menu)) return "BLOCKED: Tekla File menu does not support invoking.";

                // Re-query after opening: Tekla creates the backstage/menu elements lazily.
                AutomationElement connect = WaitForNamed(root, "Trimble Connect", 3000);
                if (connect == null)
                    return "BLOCKED: File menu opened, but Trimble Connect item was not found. " +
                        "Please open Start collaboration manually and send a UI inspection of the open File menu.";
                if (!Invoke(connect))
                    return "BLOCKED: Trimble Connect menu item cannot be invoked.";

                AutomationElement start = WaitForNamed(root, "Start collaboration", 3000);
                if (start == null)
                    return "BLOCKED: Trimble Connect menu opened, but Start collaboration was not found.";
                if (!Invoke(start))
                    return "BLOCKED: Start collaboration item cannot be invoked.";
                return "OPENED: Requested Tekla Start collaboration from its File menu.";
            }
            return "BLOCKED: No matching Tekla window found for " + expectedModelName;
        }

        private static AutomationElement WaitForNamed(AutomationElement root, string name, int timeoutMs)
        {
            var limit = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < limit)
            {
                try
                {
                    var found = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, name,
                            PropertyConditionFlags.IgnoreCase));
                    if (found != null && found.Current.IsEnabled) return found;
                    // Menus can be separate popup windows owned by the Tekla process.
                    var windows = AutomationElement.RootElement.FindAll(
                        TreeScope.Children, Condition.TrueCondition);
                    foreach (AutomationElement w in windows)
                    {
                        if (w.Current.ProcessId != root.Current.ProcessId) continue;
                        found = w.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.NameProperty, name,
                                PropertyConditionFlags.IgnoreCase));
                        if (found != null && found.Current.IsEnabled) return found;
                    }
                }
                catch (ElementNotAvailableException) { }
                System.Threading.Thread.Sleep(150);
            }
            return null;
        }

        private static bool Invoke(AutomationElement element)
        {
            object p;
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out p))
            {
                ((InvokePattern)p).Invoke();
                return true;
            }
            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out p))
            {
                ((ExpandCollapsePattern)p).Expand();
                return true;
            }
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out p))
            {
                ((SelectionItemPattern)p).Select();
                return true;
            }
            return false;
        }
    }
}
