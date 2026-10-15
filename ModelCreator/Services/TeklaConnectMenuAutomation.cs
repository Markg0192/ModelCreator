using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace ModelCreator.Services
{
    internal static class TeklaConnectMenuAutomation
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        private const uint LeftDown = 0x0002, LeftUp = 0x0004;

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
                if (!Invoke(menu) && !ClickControl(menu, process.MainWindowHandle))
                    return "BLOCKED: Could not click Tekla File menu via UI Automation or native input.";

                // Re-query after opening: Tekla creates the backstage/menu elements lazily.
                AutomationElement connect = WaitForNamed(root, "Trimble Connect", 3000);
                if (connect == null)
                    return "BLOCKED: File menu opened, but Trimble Connect item was not found. " +
                        "Please open Start collaboration manually and send a UI inspection of the open File menu.";
                if (!Invoke(connect) && !ClickControl(connect, process.MainWindowHandle))
                    return "BLOCKED: Trimble Connect menu item cannot be clicked.";

                AutomationElement start = WaitForNamed(root, "Start collaboration", 3000);
                if (start == null)
                    return "BLOCKED: Trimble Connect menu opened, but Start collaboration was not found.";
                if (!Invoke(start) && !ClickControl(start, process.MainWindowHandle))
                    return "BLOCKED: Start collaboration item cannot be clicked.";
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

        private static bool ClickControl(AutomationElement element, IntPtr teklaHandle)
        {
            // Tekla's custom ribbon/backstage buttons frequently expose no
            // InvokePattern. Use the UIA element's own visible rectangle instead
            // of hard-coded screen positions.
            if (!element.Current.IsEnabled || element.Current.IsOffscreen)
                return false;
            var rectangle = element.Current.BoundingRectangle;
            if (rectangle.IsEmpty || rectangle.Width < 2 || rectangle.Height < 2)
                return false;
            int x = (int)(rectangle.Left + rectangle.Width / 2);
            int y = (int)(rectangle.Top + rectangle.Height / 2);
            POINT previous;
            if (!GetCursorPos(out previous)) return false;
            SetForegroundWindow(teklaHandle);
            System.Threading.Thread.Sleep(120);
            if (!SetCursorPos(x, y)) return false;
            mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
            return true;
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
