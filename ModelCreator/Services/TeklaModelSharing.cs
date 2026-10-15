using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Tekla.Structures.Model;

namespace ModelCreator.Services
{
    internal static class TeklaModelSharing
    {
        // Starting sharing publishes model data to Tekla's cloud service.
        // Verify the exact model before taking this irreversible external action.
        public static async Task<string> StartAsync(string expectedPath, string code, string description, string inviteesPath)
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
            var result = await handler.StartSharingAsync(code, description).ConfigureAwait(false);
            if (result.Code != SharingResultCode.OK)
                throw new InvalidOperationException("Tekla Model Sharing failed: " +
                    result.Code + " " + result.ErrorDetails);

            // Creator is automatically Owner; additional invitations only follow
            // a confirmed successful StartSharingAsync response.
            var lines = new List<string>();
            lines.Add("Model Sharing started successfully. Model ID: " + result.Data);
            if (!string.IsNullOrWhiteSpace(inviteesPath) && File.Exists(inviteesPath))
            {
                var invitees = SharingInvitees.Validate(
                    JsonConvert.DeserializeObject<List<SharingInvitee>>(
                        File.ReadAllText(inviteesPath)) ?? new List<SharingInvitee>());
                foreach (var user in invitees)
                {
                    SharingRole role;
                    switch (user.Role)
                    {
                        case "Owner": role = SharingRole.Owner; break;
                        case "Editor": role = SharingRole.Editor; break;
                        case "Viewer": role = SharingRole.Viewer; break;
                        case "Project viewer": role = SharingRole.ProjectViewer; break;
                        default: throw new InvalidOperationException("Unknown role " + user.Role);
                    }
                    try
                    {
                        var invite = await handler.InviteUserAsync(
                            user.Email, role, true, "").ConfigureAwait(false);
                        lines.Add(user.Email + " (" + user.Role + "): " +
                            (invite.Code == SharingResultCode.OK ? "Invited" :
                            "FAILED " + invite.Code + " " + invite.ErrorDetails));
                    }
                    catch (Exception ex)
                    {
                        lines.Add(user.Email + " (" + user.Role +
                            "): UNCERTAIN " + ex.Message);
                    }
                }
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
