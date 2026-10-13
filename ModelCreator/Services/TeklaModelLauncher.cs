using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ModelCreator.Services
{
    public sealed class TeklaModelLauncher
    {
        private const string TemplateName = "SEVERFIELD_Model_Template";
        private static readonly string RootFolder = @"C:\TeklaStructuresModels2026";

        // Tekla starts asynchronously and may read its startup INI after Process.Start.
        // Conservative grace period; older files are removed on subsequent launches.
        private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromMinutes(10);

        private static void CleanOldBypassFiles(string folder)
        {
            if (!Directory.Exists(folder))
                return;

            foreach (string file in Directory.GetFiles(folder, "Bypass2026_*.ini"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(1))
                        File.Delete(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void DeleteBypassLater(string file)
        {
            // Best effort only; a closed ModelCreator process cannot finish this task.
            Task.Delay(StartupGracePeriod).ContinueWith(_ =>
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }, TaskScheduler.Default);
        }

        public string GetDestination(string modelName)
        {
            return Path.Combine(RootFolder, modelName ?? "");
        }

        private static string FindTeklaExecutable()
        {
            var candidates = new List<string>();

            // Confirmed installation path on the development workstation.
            candidates.Add(@"C:\TeklaStructures\2026.0\bin\TeklaStructures.exe");

            // The standard 2026 directory is "2026", but some installations
            // and deployment configurations use "2026.0".
            foreach (string programFiles in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            })
            {
                if (string.IsNullOrWhiteSpace(programFiles))
                    continue;

                foreach (string version in new[] { "2026", "2026.0" })
                    candidates.Add(Path.Combine(programFiles, "Tekla Structures",
                        version, "bin", "TeklaStructures.exe"));
            }

            // Some Severfield machines install Tekla directly beneath C:\\.
            foreach (string version in new[] { "2026", "2026.0", "26" })
                candidates.Add(Path.Combine(@"C:\Tekla Structures", version, "bin", "TeklaStructures.exe"));

            // Corporate deployments may use INSTALLDIR instead of Program Files.
            // The product registers its installation beneath this key.
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = baseKey.OpenSubKey(
                        @"SOFTWARE\Trimble\Tekla Structures\2026.0"))
                    {
                        if (key == null)
                            continue;

                        foreach (string valueName in new[] { "InstallDir", "INSTALLDIR", "InstallPath" })
                        {
                            string folder = key.GetValue(valueName) as string;
                            if (string.IsNullOrWhiteSpace(folder))
                                continue;

                            candidates.Add(Path.Combine(folder, "bin", "TeklaStructures.exe"));
                            candidates.Add(Path.Combine(folder, "2026", "bin", "TeklaStructures.exe"));
                            candidates.Add(Path.Combine(folder, "2026.0", "bin", "TeklaStructures.exe"));
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
            }

            foreach (string candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;

            throw new FileNotFoundException(
                "Tekla Structures 2026 executable could not be found automatically. " +
                "Right-click your Tekla Structures 2026 desktop shortcut, select Properties, " +
                "and check the Target path. Please provide that path so it can be configured.");
        }

        public void Launch(string modelName, string license)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                throw new ArgumentException("Model name is required.");

            if (license != "DIAMOND" && license != "CARBON")
                throw new ArgumentException("Unsupported Tekla licence selection.");

            string destination = GetDestination(modelName);
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new IOException(
                    "A model or file already exists at:\n" + destination +
                    "\n\nNo files have been changed.");

            string executable = FindTeklaExecutable();
            // Derive environment location from the executable installation, not a hardcoded version.
            string versionRoot = Directory.GetParent(Path.GetDirectoryName(executable)).FullName;
            string dataRoot = Path.Combine(versionRoot, "Environments", "uk");
            string envPath = Path.Combine(dataRoot, "env_UK.ini");
            string rolePath = Path.Combine(dataRoot, "role_SEVERFIELD.ini");
            if (!File.Exists(envPath))
                throw new FileNotFoundException("UK environment was not found: " + envPath);
            if (!File.Exists(rolePath))
                throw new FileNotFoundException("Severfield role was not found: " + rolePath);

            string configFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ModelCreator");
            Directory.CreateDirectory(configFolder);
            CleanOldBypassFiles(configFolder);
            string bypassPath = Path.Combine(configFolder,
                "Bypass2026_" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllLines(bypassPath, new[]
            {
                "set XS_DEFAULT_ENVIRONMENT=" + envPath,
                "set XS_DEFAULT_ROLE=" + rolePath,
                "set XS_DEFAULT_LICENSE=" + license
            });
            Directory.CreateDirectory(RootFolder);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "-I " + (char)34 + bypassPath + (char)34 +
                            " /create:" + (char)34 + destination + (char)34 +
                            " /modelTemplate:" + (char)34 + TemplateName + (char)34,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
            };

            try
            {
                Process process = Process.Start(startInfo);
                if (process == null)
                    throw new InvalidOperationException("Tekla did not start.");
                DeleteBypassLater(bypassPath);
            }
            catch
            {
                try { File.Delete(bypassPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
}
