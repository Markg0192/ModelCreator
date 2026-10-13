using System;
using System.IO;
using Tekla.Structures.Model;

namespace ModelCreator.Services
{
    internal static class TeklaProjectProperties
    {
        // Never update properties unless the connected model path matches the requested model.
        public static bool TryApply(string expectedPath, string contractNumber,
            string projectName, out string diagnostic)
        {
            diagnostic = "Attempting to connect to Tekla...";
            Model model = new Model();

            if (!model.GetConnectionStatus())
            {
                diagnostic = "Tekla API connection not ready (GetConnectionStatus is false). " +
                             "The model may be open in a different Tekla instance.";
                return false;
            }

            ModelInfo modelInfo = model.GetInfo();
            if (modelInfo == null || string.IsNullOrWhiteSpace(modelInfo.ModelPath))
            {
                diagnostic = "Connected to Tekla, but the model path is not yet available.";
                return false;
            }

            string actual = Path.GetFullPath(modelInfo.ModelPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string expected = Path.GetFullPath(expectedPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "Connected to Tekla, but model path does not match. " +
                             "Connected: " + actual + " | Expected: " + expected;
                return false;
            }

            diagnostic = "Correct model detected. Applying project properties...";
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
