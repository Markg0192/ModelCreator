using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ModelCreator.Services;

namespace ModelCreator
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Separate process per attempt: a connection made before Tekla is ready
            // cannot leave stale Open API state in the UI process.
            if (e.Args.Length == 5 && e.Args[0] == "--apply-project-properties")
            {
                try
                {
                    string diagnostic;
                    bool applied = TeklaProjectProperties.TryApply(
                        e.Args[1], e.Args[2], e.Args[3], out diagnostic);
                    File.WriteAllText(e.Args[4],
                        (applied ? "OK" : "WAIT") + Environment.NewLine + diagnostic);
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.WriteAllText(e.Args[4],
                            "ERROR" + Environment.NewLine + ex.ToString());
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                Shutdown();
                return;
            }

            if (e.Args.Length == 5 && e.Args[0] == "--start-sharing")
            {
                try
                {
                    // Do not block the WPF dispatcher while waiting for Tekla's async
                    // sharing operation: continuations may need that context.
                    string result = Task.Run(() => TeklaModelSharing.StartAsync(
                        e.Args[1], e.Args[2], e.Args[3]       )).GetAwaiter().GetResult();
                    File.WriteAllText(e.Args[4], "OK" + Environment.NewLine + result);
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.WriteAllText(e.Args[4], "ERROR" + Environment.NewLine + ex.ToString());
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                Shutdown();
                return;
            }

            base.OnStartup(e);
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }
}
