using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace ModelCreator.Services
{
    internal static class TrimbleConnectAccess
    {
        // Do not store access tokens in source control or model folders.
        // A real production integration will obtain and refresh tokens using
        // Trimble Identity OAuth Authorization Code + PKCE.
        public static async Task<string> DiagnoseAsync()
        {
            string token = Environment.GetEnvironmentVariable("TRIMBLE_CONNECT_ACCESS_TOKEN");
            string clientId = Environment.GetEnvironmentVariable("TRIMBLE_CONNECT_CLIENT_ID");

            if (string.IsNullOrWhiteSpace(token))
            {
                return "No Trimble Connect API access token is configured for ModelCreator. " +
                    (string.IsNullOrWhiteSpace(clientId)
                        ? "No registered application client ID was found either. "
                        : "A registered client ID was found, but OAuth login is still required. ") +
                    "Tekla's existing sign-in does not automatically authorize this standalone app. " +
                    "Register or identify the Trimble Connect API application and complete OAuth login before project creation. " +
                    "No cloud project was created.";
            }

            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token.Trim());
                using (HttpResponseMessage response =
                    await client.GetAsync("https://app.connect.trimble.com/tc/api/2.0/users/me")
                        .ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.Forbidden)
                        return "Trimble Connect rejected the configured access token (" +
                            (int)response.StatusCode + "). Sign in via Trimble Identity OAuth; no project was created.";

                    if (!response.IsSuccessStatusCode)
                        return "Trimble Connect API returned HTTP " + (int)response.StatusCode +
                            " while validating the user. No project was created.";

                    return "Trimble Connect API authentication succeeded. " +
                        "The next step is to verify the Severfield licence entitlement " +
                        "and implement project creation and Tekla model linking. " +
                        "No project has been created by this diagnostic.";
                }
            }
        }
    }
}
