using System;
using System.IO;
using Tekla.Structures.Model.Operations;

namespace ModelCreator.Services
{
    internal static class TeklaConnectCollaboration
    {
        // A user-recorded Tekla macro must be installed in XS_MACRO_DIRECTORY.
        // Do not fabricate internal dialog commands or manipulate TConnect files.
        private const string MacroName = "ModelCreatorStartConnect.cs";

        internal static string LaunchRecordedMacro(string expectedPath)
        {
            string diagnostic;
            string modelPath = TeklaProjectProperties.GetConnectedModelPath(out diagnostic);
            if (modelPath == null)
                return "WAIT: " + diagnostic;
            string expected = Path.GetFullPath(expectedPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(expected, modelPath, StringComparison.OrdinalIgnoreCase))
                return "ERROR: Different Tekla model is open. Connected: " + modelPath;

            try
            {
                bool launched = Operation.RunMacro(MacroName);
                return launched
                    ? "OK: Tekla collaboration macro launched. Complete and verify the " +
                      "built-in Europe / Severfield Business Premium project creation dialog in Tekla. " +
                      "This does not yet verify that a project was created or linked."
                    : "MISSING: Record a Tekla macro that opens File > Trimble Connect > " +
                      "Start collaboration and save it as " + MacroName +
                      " in the model macro directory. Then retry. No project created.";
            }
            catch (Exception ex)
            {
                return "MISSING: The recorded Tekla macro is not available: " + ex.Message +
                    ". Record a macro opening File > Trimble Connect > Start collaboration, " +
                    "save it as " + MacroName + ", and retry. No project created.";
            }
        }
    }
}
