using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
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
                AutomationElement connect = WaitForFileMenuItem(root, "Trimble Connect", 3000);
                if (connect == null)
                    return "BLOCKED: File menu opened, but Trimble Connect item was not found. " +
                        "Please open Start collaboration manually and send a UI inspection of the open File menu.";
                if (!Invoke(connect) && !ClickControl(connect, process.MainWindowHandle))
                    return "BLOCKED: Trimble Connect menu item cannot be clicked.";

                AutomationElement start = WaitForNamed(root, "Start collaboration", 4500, true);
                if (start == null)
                    return "BLOCKED: Start collaboration was not found in the visible Tekla menu. " +
                        "Visible menu controls: " + DescribeVisibleControls(root);
                if (!Invoke(start) && !ClickControl(start, process.MainWindowHandle))
                    return "BLOCKED: Start collaboration item cannot be clicked.";
                return "OPENED: Requested Tekla Start collaboration from its File menu.";
            }
            return "BLOCKED: No matching Tekla window found for " + expectedModelName;
        }

        private static AutomationElement WaitForFileMenuItem(
            AutomationElement root, string name, int timeoutMs)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                try
                {
                    // Tekla backstage entries are often Text or Custom controls,
                    // NOT standard MenuItem controls. Anchor the search to the
                    // visible File menu's vertical panel, never the ribbon tab.
                    var menuLabel = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, "Modeling"));
                    var matches = root.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, name,
                            PropertyConditionFlags.IgnoreCase));
                    foreach (AutomationElement item in matches)
                    {
                        if (!IsUsable(item, true)) continue;
                        var rect = item.Current.BoundingRectangle;
                        if (rect.Left < 0 || rect.Left > 360 || rect.Top < 0)
                            continue;
                        if (item.Current.ControlType == ControlType.Text ||
                            item.Current.ControlType == ControlType.MenuItem ||
                            item.Current.ControlType == ControlType.ListItem)
                            return item;
                    }
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                System.Threading.Thread.Sleep(150);
            }
            return null;
        }

        private static AutomationElement WaitForNamed(AutomationElement root, string name,
            int timeoutMs, bool requireVisible)
        {
            var limit = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < limit)
            {
                try
                {
                    var candidates = root.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, name,
                            PropertyConditionFlags.IgnoreCase));
                    foreach (AutomationElement candidate in candidates)
                        if (IsUsable(candidate, requireVisible)) return candidate;

                    var windows = AutomationElement.RootElement.FindAll(
                        TreeScope.Children, Condition.TrueCondition);
                    foreach (AutomationElement w in windows)
                    {
                        if (w.Current.ProcessId != root.Current.ProcessId) continue;
                        candidates = w.FindAll(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.NameProperty, name,
                                PropertyConditionFlags.IgnoreCase));
                        foreach (AutomationElement candidate in candidates)
                            if (IsUsable(candidate, requireVisible)) return candidate;
                    }
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                System.Threading.Thread.Sleep(150);
            }
            return null;
        }

        private static bool IsUsable(AutomationElement element, bool requireVisible)
        {
            if (!element.Current.IsEnabled) return false;
            if (!requireVisible) return true;
            var rect = element.Current.BoundingRectangle;
            return !element.Current.IsOffscreen && !rect.IsEmpty &&
                rect.Width > 2 && rect.Height > 2;
        }

        private static string DescribeVisibleControls(AutomationElement root)
        {
            var found = new List<string>();
            try
            {
                var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                foreach (AutomationElement el in all)
                {
                    if (found.Count >= 35) break;
                    try
                    {
                        if (el.Current.IsOffscreen) continue;
                        string name = el.Current.Name;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var type = el.Current.ControlType;
                        if (type != ControlType.MenuItem && type != ControlType.Button &&
                            type != ControlType.Text && type != ControlType.ListItem) continue;
                        if (!found.Contains(name)) found.Add(name);
                    }
                    catch (ElementNotAvailableException) { }
                }
            }
            catch (ElementNotAvailableException) { }
            return found.Count == 0 ? "(none)" : string.Join(" | ", found);
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
