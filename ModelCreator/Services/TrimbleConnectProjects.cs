using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ModelCreator.Services
{
    internal static class TrimbleConnectProjects
    {
        private const string EuropeBase = "https://app21.connect.trimble.com/tc/api/2.0/";
        private const string CompanyAccount = "29b87e21-2b12-46e5-aef0-1b1df2958f08";

        // Called only after project creation has returned a verified Europe rootFolderId.
        // Never create folders if a project/region cannot be confirmed.
        public static async Task CreateTestFoldersAsync(string accessToken, string rootFolderId)
        {
            if (string.IsNullOrWhiteSpace(rootFolderId))
                throw new ArgumentException("Project root folder ID is required.");

            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
                foreach (string name in new[] { "test 1", "test 2" })
                {
                    var payload = new JObject
                    {
                        ["name"] = name,
                        ["parentId"] = rootFolderId
                    };
                    using (var body = new StringContent(payload.ToString(),
                        Encoding.UTF8, "application/json"))
                    using (var response = await http.PostAsync(EuropeBase + "folders", body)
                        .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException(
                                "Could not create folder '" + name + "' (HTTP " +
                                (int)response.StatusCode + ").");
                    }
                }
            }
        }

        // Examine a small sample of existing project details via the documented
        // GET /projects/{id} API. Never request files, members or change projects.
        public static async Task<string> InspectProjectOwnershipAsync(string accessToken)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) })
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
                using (HttpResponseMessage listing = await http.GetAsync(
                    EuropeBase + "projects?fullyLoaded=false").ConfigureAwait(false))
                {
                    if (!listing.IsSuccessStatusCode)
                        return "Europe project list returned HTTP " + (int)listing.StatusCode;
                    JToken json = JToken.Parse(await listing.Content.ReadAsStringAsync()
                        .ConfigureAwait(false));
                    JArray projects = json as JArray ?? json["projects"] as JArray ??
                        json["items"] as JArray;
                    if (projects == null)
                        return "Could not interpret the project list format.";

                    var report = new System.Collections.Generic.List<string>();
                    foreach (JToken entry in projects.Take(8))
                    {
                        string id = (string)entry["id"];
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        using (HttpResponseMessage response = await http.GetAsync(
                            EuropeBase + "projects/" + Uri.EscapeDataString(id))
                            .ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                report.Add("Project details HTTP " + (int)response.StatusCode);
                                continue;
                            }
                            JObject detail = JObject.Parse(await response.Content
                                .ReadAsStringAsync().ConfigureAwait(false));
                            string accountId = (string)detail["accountId"] ??
                                (string)detail["account"]?["id"];
                            string licenseId = (string)detail["licenseId"] ??
                                (string)detail["license"]?["id"];
                            string license = (string)detail["licenseName"] ??
                                (string)detail["license"]?["name"];
                            if (accountId != null || licenseId != null || license != null)
                                report.Add("Project: " + ((string)detail["name"] ?? "(unnamed)") +
                                    "; accountId=" + (accountId ?? "(unavailable)") +
                                    "; licenseId=" + (licenseId ?? "(unavailable)") +
                                    "; license=" + (license ?? "(unavailable)") +
                                    "; licence-related fields: " + string.Join(", ",
                                        detail.Properties().Where(p =>
                                            p.Name.IndexOf("licen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            p.Name.IndexOf("account", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            p.Name.IndexOf("entitle", StringComparison.OrdinalIgnoreCase) >= 0)
                                            .Select(p => p.Name)));
                        }
                    }
                    return report.Count == 0
                        ? "Existing project detail responses did not expose accountId/licenseId " +
                          "in the standard fields; the creation licence remains unverified."
                        : "Existing Europe project ownership metadata (read-only):" +
                            Environment.NewLine + string.Join(Environment.NewLine, report);
                }
            }
        }

        // Read-only preflight: we cannot safely create an owned project until
        // the concrete Severfield entitlement/license ID is validated.
        public static async Task<string> PreflightAsync(string accessToken, string exactModelName)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);

                using (HttpResponseMessage response = await http.GetAsync(
                    EuropeBase + "projects?fullyLoaded=false").ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Europe project lookup failed: HTTP " +
                            (int)response.StatusCode + ". No project created.");

                    JToken json = JToken.Parse(body);
                    var projects = json.Type == JTokenType.Array ? (JArray)json :
                        (json["projects"] as JArray ?? json["items"] as JArray);
                    if (projects == null)
                        throw new InvalidOperationException(
                            "Unexpected Europe projects response. No changes made.");

                    var matches = projects.Where(p => string.Equals(
                        (string)p["name"], exactModelName, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count > 0)
                        return "A Europe Trimble Connect project already uses the name '" +
                            exactModelName + "' (" + matches.Count +
                            " match(es)). No duplicate created. Verify the project ownership and link.";

                    return "Connected to the Europe Trimble Connect API successfully. " +
                        "No existing project named '" + exactModelName + "' was returned. " +
                        "Severfield account: " + CompanyAccount + ". " +
                        "Project creation and test folders are not executed until the actual " +
                        "Severfield Business Premium licence/entitlement identifier is verified. " +
                        "The account ID is not a licence ID.";
                }
            }
        }
    }
}
