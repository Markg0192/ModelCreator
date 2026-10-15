using System;
using System.IO;
using Tekla.Structures.Model;

namespace ModelCreator.Services
{
    internal static class TeklaProjectProperties
    {
        public static string GetConnectedModelPath(out string diagnostic)
        {
            diagnostic = "Attempting to connect to Tekla...";
            Model model = new Model();

            if (!model.GetConnectionStatus())
            {
                diagnostic = "Tekla API connection not ready (GetConnectionStatus is false). " +
                             "Check that a model is open and that the API is connecting to the expected Tekla instance.";
                return null;
            }

            ModelInfo info = model.GetInfo();
            if (info == null || string.IsNullOrWhiteSpace(info.ModelPath))
            {
                diagnostic = "Connected to Tekla, but the model path is not available.";
                return null;
            }

            diagnostic = "Connected to Tekla model: " + info.ModelPath;
            return Path.GetFullPath(info.ModelPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        // Never update properties unless the connected model path matches the requested model.
        public static bool TryApply(string expectedPath, string contractNumber,
            string projectName, out string diagnostic)
        {
            string actual = GetConnectedModelPath(out diagnostic);
            if (actual == null)
                return false;

            string expected = Path.GetFullPath(expectedPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "Connected to Tekla, but model path does not match. " +
                             "Connected: " + actual + " | Expected: " + expected;
                return false;
            }

            diagnostic = "Correct model detected. Applying project properties...";
            Model model = new Model();
            if (!model.GetConnectionStatus())
            {
                diagnostic = "Tekla connection was lost before applying project properties.";
                return false;
            }

            ModelInfo confirmedInfo = model.GetInfo();
            if (confirmedInfo == null || string.IsNullOrWhiteSpace(confirmedInfo.ModelPath))
            {
                diagnostic = "Could not confirm the connected model before writing.";
                return false;
            }

            string confirmedPath = Path.GetFullPath(confirmedInfo.ModelPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(confirmedPath, expected, StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "The connected model changed before writing. No changes made.";
                return false;
            }

            ProjectInfo project = model.GetProjectInfo();
            if (project == null)
                throw new InvalidOperationException("Tekla returned no project properties.");

            project.ProjectNumber = contractNumber;
            project.Name = projectName;

            if (!project.Modify())
                throw new InvalidOperationException("Tekla did not accept the project property changes.");

            if (!model.CommitChanges())
                throw new InvalidOperationException("Tekla could not save the project property changes.");

            diagnostic = "Project properties saved.";
            return true;
        }
    }
}
