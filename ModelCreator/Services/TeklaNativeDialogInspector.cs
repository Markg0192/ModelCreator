using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace ModelCreator.Services
{
    // Native Win32 enumeration catches owned/modally nested dialogs that
    // AutomationElement.RootElement/TreeScope.Children can fail to expose.
    internal static class TeklaNativeDialogInspector
    {
        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        internal static string WatchForDialog(TimeSpan duration)
        {
            var observations = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deadline = DateTime.UtcNow.Add(duration);
            var lastConnectorCount = 0;
            while (DateTime.UtcNow < deadline)
            {
                var connectorPids = new HashSet<uint>();
                foreach (var process in Process.GetProcessesByName("TrimbleConnector"))
                    connectorPids.Add((uint)process.Id);
                lastConnectorCount = connectorPids.Count;
                bool found = false;
                EnumWindows((hwnd, unused) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    string title = WindowText(hwnd);
                    bool connectorOwned = connectorPids.Contains(pid);
                    bool titled = title.IndexOf("Trimble Connector",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                        title.IndexOf("Select project", StringComparison.OrdinalIgnoreCase) >= 0;
                    // A connector-owned WPF window can have an empty native title.
                    if (!connectorOwned && !titled) return true;
                    bool visible = IsWindowVisible(hwnd);
                    string info = "HWND=0x" + hwnd.ToInt64().ToString("X") +
                        " PID=" + pid + " visible=" + visible +
                        " class=" + ClassName(hwnd) + " title='" + title + "'";
                    if (seen.Add(info)) observations.Add(info);
                    if (connectorOwned && visible) found = true;
                    if (titled && visible) found = true;
                    return true;
                }, IntPtr.Zero);
                if (found)
                    return "Connector-owned window detected.\n" +
                        string.Join("\n", observations) + "\n\n" + Inspect();
                System.Threading.Thread.Sleep(200);
            }
            return "No visible connector-owned window detected in " +
                duration.TotalSeconds + " seconds. TrimbleConnector process count=" +
                lastConnectorCount + ".\nObserved matching windows:\n" +
                (observations.Count == 0 ? "(none)" :
                    string.Join("\n", observations)) +
                "\nNo project created.";
        }

        internal static string Inspect()
        {
            var lines = new List<string>();
            lines.Add("Native Tekla collaboration window discovery (read-only):");
            var pids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName("TeklaStructures"))
                pids.Add((uint)p.Id);
            foreach (var p in Process.GetProcessesByName("TrimbleConnector"))
                pids.Add((uint)p.Id);
            lines.Add("TrimbleConnector processes: " +
                Process.GetProcessesByName("TrimbleConnector").Length);
            int matching = 0;
            EnumWindows((hwnd, unused) =>
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                string title = WindowText(hwnd);
                bool matches = title.IndexOf("Trimble Connector", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               title.IndexOf("Select project", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!matches && !pids.Contains(pid)) return true;
                if (!IsWindowVisible(hwnd)) return true;
                matching++;
                lines.Add("HWND=0x" + hwnd.ToInt64().ToString("X") + " PID=" + pid +
                    " class=" + ClassName(hwnd) + " title=" + title);
                var children = new List<string>();
                EnumChildWindows(hwnd, (child, unused2) =>
                {
                    if (children.Count >= 90) return false;
                    string name = WindowText(child);
                    if (!string.IsNullOrWhiteSpace(name) || children.Count < 20)
                        children.Add("  child 0x" + child.ToInt64().ToString("X") +
                            " class=" + ClassName(child) + " text=" + name);
                    return true;
                }, IntPtr.Zero);
                lines.AddRange(children);
                try
                {
                    var root = AutomationElement.FromHandle(hwnd);
                    var descendants = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                    int cap = Math.Min(descendants.Count, 160);
                    for (int i = 0; i < cap; i++)
                    {
                        try
                        {
                            var el = descendants[i];
                            string name = el.Current.Name ?? "";
                            string id = el.Current.AutomationId ?? "";
                            if (name.Length > 0 || id.Length > 0)
                                lines.Add("  UIA " + el.Current.ControlType.ProgrammaticName +
                                    " name=" + name + " id=" + id);
                        }
                        catch (ElementNotAvailableException) { }
                    }
                }
                catch (Exception ex)
                {
                    lines.Add("  UIA failed: " + ex.GetType().Name);
                }
                return true;
            }, IntPtr.Zero);
            lines.Add("Matching visible windows: " + matching);
            lines.Add("Read-only: no Create button has been pressed.");
            return string.Join(Environment.NewLine, lines);
        }

        private static string WindowText(IntPtr hwnd)
        {
            var value = new StringBuilder(Math.Max(2, Math.Min(512, GetWindowTextLength(hwnd) + 2)));
            GetWindowText(hwnd, value, value.Capacity);
            return value.ToString();
        }
        private static string ClassName(IntPtr hwnd)
        {
            var value = new StringBuilder(256);
            GetClassName(hwnd, value, value.Capacity);
            return value.ToString();
        }
    }
}
