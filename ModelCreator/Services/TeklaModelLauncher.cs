using System;
using System.Diagnostics;
using System.IO;

namespace ModelCreator.Services
{
    public sealed class TeklaModelLauncher
    {
        private const string TemplateName = "SEVERFIELD_Model_Template";
        private static readonly string RootFolder =
            @"C:\Tekla Structures Models 2026";
        private static readonly string Executable =
            @"C:\Program Files\Tekla Structures\2026.0\bin\TeklaStructures.exe";

        public string GetDestination(string modelName)
        {
            return Path.Combine(RootFolder, modelName ?? "");
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

            if (!File.Exists(Executable))
                throw new FileNotFoundException(
                    "Tekla Structures 2026 was not found at its expected location. " +
                    "Check the installation path in TeklaModelLauncher.cs.", Executable);

            // Tekla must create its own model directory. Never create the leaf folder here.
            Directory.CreateDirectory(RootFolder);

            var startInfo = new ProcessStartInfo
            {
                FileName = Executable,
                Arguments = "/create:\"" + destination + "\" /modelTemplate:\"" +
                            TemplateName + "\"",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(Executable)
            };

            // Launch is asynchronous: success here means the process started,
            // not that Tekla has finished creating the model.
            Process.Start(startInfo);
        }
    }
}
