using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ModelCreator.Services
{
    public sealed class TeklaModelLauncher
    {
        private const string TemplateName = "SEVERFIELD_Model_Template";
        private static readonly string RootFolder = @"C:\TeklaStructuresModels2026";

        public string GetDestination(string modelName)
        {
            return Path.Combine(RootFolder, modelName ?? "");
        }

        private static string FindTeklaExecutable()
        {
            var candidates = new List<string>();

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

        public void Launch(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                throw new ArgumentException("Model name is required.");

            string destination = GetDestination(modelName);
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new IOException(
                    "A model or file already exists at:\n" + destination +
                    "\n\nNo files have been changed.");

            string executable = FindTeklaExecutable();
            Directory.CreateDirectory(RootFolder);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "/create:\\"" + destination + "\\" /modelTemplate:\\"" +
                            TemplateName + "\\"",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
            };

            Process.Start(startInfo);
        }
    }
}
