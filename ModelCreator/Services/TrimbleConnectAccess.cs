using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace ModelCreator.Services
{
    internal static class TrimbleConnectAccess
    {
        // Validates the just-acquired user token without persisting it to disk.
        public static async Task<string> DiagnoseAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "No OAuth token returned. No cloud project created.";

            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
                using (HttpResponseMessage response =
                    await client.GetAsync("https://app.connect.trimble.com/tc/api/2.0/users/me")
                        .ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                        return "Trimble rejected the token (HTTP " + (int)response.StatusCode +
                            "). No project created.";

                    if (!response.IsSuccessStatusCode)
                        return "Trimble user check returned HTTP " + (int)response.StatusCode +
                            ". No project created.";

                    return "Trimble Connect sign-in and API authentication succeeded. " +
                        "The test does not yet create/link a project: the Europe project " +
                        "creation payload and supported Tekla project-link mechanism " +
                        "still need to be verified. No project created.";
                }
            }
        }
    }
}
