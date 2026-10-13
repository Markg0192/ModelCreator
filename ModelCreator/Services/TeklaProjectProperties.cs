using System;
using System.IO;
using Tekla.Structures.Model;

namespace ModelCreator.Services
{
    internal static class TeklaProjectProperties
    {
        // Returns false until Tekla has opened exactly the model created by ModelCreator.
        // Never stamp a different model, even if Tekla is already running.
        public static bool TryApply(string expectedPath, string contractNumber, string projectName)
        {
            Model model = new Model();
            if (!model.GetConnectionStatus())
                return false;

            ModelInfo modelInfo = model.GetInfo();
            if (modelInfo == null || string.IsNullOrWhiteSpace(modelInfo.ModelPath))
                return false;

            string actual = Path.GetFullPath(modelInfo.ModelPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string expected = Path.GetFullPath(expectedPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return false;

            ProjectInfo project = model.GetProjectInfo();
            if (project == null)
                throw new InvalidOperationException("Tekla returned no project properties.");

            project.ProjectNumber = contractNumber;
            project.Name = projectName;

            if (!project.Modify())
                throw new InvalidOperationException("Tekla did not accept the project property changes.");

            if (!model.CommitChanges())
                throw new InvalidOperationException("Tekla could not save the project property changes.");

            return true;
        }
    }
}
