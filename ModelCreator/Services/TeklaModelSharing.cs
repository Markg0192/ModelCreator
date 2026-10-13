using System;
using System.IO;
using System.Threading.Tasks;
using Tekla.Structures.Model;

namespace ModelCreator.Services
{
    internal static class TeklaModelSharing
    {
        // Starting sharing publishes model data to Tekla's cloud service.
        // Verify the exact model before taking this irreversible external action.
        public static async Task<string> StartAsync(string expectedPath, string code, string description)
        {
            string diagnostic;
            string connectedPath = TeklaProjectProperties.GetConnectedModelPath(out diagnostic);
            if (connectedPath == null)
                throw new InvalidOperationException(diagnostic);

            string expected = Path.GetFullPath(expectedPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(connectedPath, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Wrong Tekla model. Connected: " +
                    connectedPath + " | Expected: " + expected);

            var handler = new ModelSharingHandler();
            var result = await handler.StartSharingAsync(code, description);
            if (result.Code != SharingResultCode.OK)
                throw new InvalidOperationException("Tekla Model Sharing failed: " +
                    result.Code + " " + result.ErrorDetails);

            return "Model Sharing started successfully. Model ID: " + result.Data;
        }
    }
}
