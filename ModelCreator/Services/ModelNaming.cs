using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ModelCreator.Services
{
    public static class ModelNaming
    {
        // Make the contract pattern configurable once Workspace is integrated.
        private static readonly Regex ContractPattern =
            new Regex(@"^C\d{4}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string BuildPreview(string contractNumber, string projectName)
        {
            string contract = (contractNumber ?? "").Trim().ToUpperInvariant();
            string project = (projectName ?? "").Trim();
            return string.IsNullOrEmpty(contract) && string.IsNullOrEmpty(project)
                ? "" : contract + "-" + project;
        }

        public static string ValidateAndBuild(string contractNumber, string projectName)
        {
            string contract = (contractNumber ?? "").Trim().ToUpperInvariant();
            string project = (projectName ?? "").Trim();

            if (!ContractPattern.IsMatch(contract))
                throw new ArgumentException("Enter a contract number such as C1234.");

            if (string.IsNullOrWhiteSpace(project))
                throw new ArgumentException("Enter a project name.");

            if (project.EndsWith(".", StringComparison.Ordinal) ||
                project.EndsWith(" ", StringComparison.Ordinal))
                throw new ArgumentException("Project names cannot end with a dot or space.");

            if (project.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                project.IndexOfAny(new[] { '/', '\\', ';', ':', '|' }) >= 0 ||
                project.Contains("\""))
                throw new ArgumentException("Project name contains unsupported characters.");

            string modelName = contract + "-" + project;
            if (modelName.Length > 40)
                throw new ArgumentException(
                    "The model name exceeds Tekla's 40-character limit (" +
                    modelName.Length + " characters). Shorten the project name.");

            // Windows reserves these device names even when they have an extension.
            foreach (string part in project.Split(new[] { ' ', '.', '-' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",
                    RegexOptions.IgnoreCase))
                    throw new ArgumentException("Project name contains a reserved Windows name.");
            }

            return modelName;
        }
    }
}
