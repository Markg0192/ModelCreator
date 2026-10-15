using System;
using System.Collections.Generic;
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

        private static bool TryParseProjectCreationDate(JToken value, out DateTimeOffset result)
        {
            result = default(DateTimeOffset);
            if (value == null || value.Type == JTokenType.Null) return false;
            if (value.Type == JTokenType.Date)
            {
                result = value.ToObject<DateTimeOffset>().ToUniversalTime();
                return true;
            }
            string raw = value.ToString().Trim();
            long epoch;
            if (long.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out epoch))
            {
                try
                {
                    // Trimble endpoints may return Unix seconds or milliseconds.
                    result = (Math.Abs((double)epoch) >= 100000000000.0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                        : DateTimeOffset.FromUnixTimeSeconds(epoch)).ToUniversalTime();
                    return result.Year >= 2000 && result.Year <= 2100;
                }
                catch (ArgumentOutOfRangeException) { return false; }
            }
            if (DateTimeOffset.TryParse(raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal, out result))
                return result.Year >= 2000 && result.Year <= 2100;
            return false;
        }

        // Operates only on an existing uniquely named Europe project. No new project POST.
        public static async Task<string> AddTestFoldersToExistingProjectAsync(
            string accessToken, string exactName, List<SharingInvitee> invitees = null)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
                using (var listing = await http.GetAsync(
                    EuropeBase + "projects?fullyLoaded=false").ConfigureAwait(false))
                {
                    if (!listing.IsSuccessStatusCode)
                        return "BLOCKED: Europe project list HTTP " + (int)listing.StatusCode;
                    JToken response = JToken.Parse(await listing.Content.ReadAsStringAsync()
                        .ConfigureAwait(false));
                    JArray items = response as JArray ?? response["projects"] as JArray ??
                        response["items"] as JArray;
                    if (items == null) return "BLOCKED: Unexpected Europe project list.";
                    var matches = items.Where(p => string.Equals((string)p["name"],
                        exactName, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count == 0)
                        return "BLOCKED: No Europe project named '" + exactName + "' found.";

                    // Do not assume the API list is ordered newest first.
                    // Request each matching project's details to find a reliable date.
                    var candidates = new System.Collections.Generic.List<Tuple<string, DateTimeOffset>>();
                    foreach (JToken match in matches)
                    {
                        string candidateId = (string)match["id"];
                        if (string.IsNullOrWhiteSpace(candidateId)) continue;
                        using (var candidateResponse = await http.GetAsync(
                            EuropeBase + "projects/" + Uri.EscapeDataString(candidateId))
                            .ConfigureAwait(false))
                        {
                            if (!candidateResponse.IsSuccessStatusCode)
                                return "BLOCKED: Cannot verify all duplicate projects (HTTP " +
                                    (int)candidateResponse.StatusCode + "). No folders created.";
                            JObject candidate = JObject.Parse(await candidateResponse.Content
                                .ReadAsStringAsync().ConfigureAwait(false));
                            JToken createdValue = candidate["createdOn"] ??
                                candidate["createdAt"] ??
                                candidate["creationDate"] ??
                                candidate["created"];
                            DateTimeOffset createdOn;
                            if (!TryParseProjectCreationDate(createdValue, out createdOn))
                                return "BLOCKED: Project '" + candidateId +
                                    "' has an unsupported createdOn value (" +
                                    (createdValue == null ? "missing" : createdValue.Type.ToString()) +
                                    "): " + (createdValue == null ? "(null)" :
                                        createdValue.ToString(Newtonsoft.Json.Formatting.None)) +
                                    ". No folders created.";
                            candidates.Add(Tuple.Create(candidateId, createdOn));
                        }
                    }
                    if (candidates.Count != matches.Count)
                        return "BLOCKED: Could not verify every duplicate project.";
                    var newest = candidates.OrderByDescending(x => x.Item2).ToList();
                    if (newest.Count > 1 && newest[0].Item2 == newest[1].Item2)
                        return "BLOCKED: Two projects have identical latest creation dates. " +
                            "Cannot determine newest safely.";
                    string id = newest[0].Item1;
                    string selectionNote = "Found " + matches.Count + " matching Europe projects. " +
                        "Selected newest: " + id + ", created " +
                        newest[0].Item2.ToString("u") + ".";
                    using (var detailsResponse = await http.GetAsync(
                        EuropeBase + "projects/" + Uri.EscapeDataString(id)).ConfigureAwait(false))
                    {
                        if (!detailsResponse.IsSuccessStatusCode)
                            return "BLOCKED: Could not verify project, HTTP " +
                                (int)detailsResponse.StatusCode;
                        JObject detail = JObject.Parse(await detailsResponse.Content
                            .ReadAsStringAsync().ConfigureAwait(false));
                        var licence = detail["license"];
                        string organization = (string)licence?["orgName"];
                        string sku = (string)licence?["sku"];
                        if (!string.Equals(organization, "Severfield plc",
                            StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(sku, "TrimbleConnectBusinessPremium",
                            StringComparison.OrdinalIgnoreCase))
                            return "BLOCKED: Existing project licence differs from " +
                                "Severfield Business Premium. orgName=" + organization +
                                ", sku=" + sku + ". No folders created.";

                        string rootId = (string)detail["rootFolderId"] ??
                            (string)detail["rootFolder"]?["id"] ??
                            (string)detail["rootId"];
                        if (string.IsNullOrWhiteSpace(rootId))
                            return "BLOCKED: Found the verified Severfield project '" +
                                exactName + "' (ID " + id +
                                "), but project details did not expose rootFolderId. " +
                                "Available project fields: " +
                                string.Join(", ", detail.Properties().Select(x => x.Name)) +
                                ". No folder creation attempted.";
                        var report = new StringBuilder();
                        report.AppendLine(selectionNote);
                        report.AppendLine("Verified Europe project: " + exactName + " (" + id + ")");
                        report.AppendLine("Verified licence: " + organization + " / " + sku);
                        report.AppendLine("Root folder ID: " + rootId);
                        foreach (string folder in new[] { "test 1", "test 2" })
                        {
                            // Each POST is sent at most once; an ambiguous response
                            // is reported for manual inspection, never retried.
                            var payload = new JObject
                            {
                                ["name"] = folder,
                                ["parentId"] = rootId,
                                ["projectId"] = id
                            };
                            try
                            {
                                using (var json = new StringContent(payload.ToString(),
                                    Encoding.UTF8, "application/json"))
                                using (var created = await http.PostAsync(
                                    EuropeBase + "folders", json).ConfigureAwait(false))
                                {
                                    string result = await created.Content.ReadAsStringAsync()
                                        .ConfigureAwait(false);
                                    if (!created.IsSuccessStatusCode)
                                    {
                                        report.AppendLine(folder + ": HTTP " +
                                            (int)created.StatusCode + "; " +
                                            result.Substring(0, Math.Min(250, result.Length)));
                                        report.AppendLine("STOPPED; inspect project folders " +
                                            "before trying again.");
                                        break;
                                    }
                                    report.AppendLine(folder + ": POST accepted, HTTP " +
                                        (int)created.StatusCode);
                                }
                            }
                            catch (Exception ex)
                            {
                                report.AppendLine(folder + ": outcome uncertain (" +
                                    ex.GetType().Name + ": " + ex.Message + "). " +
                                    "Check Connect before retrying.");
                                break;
                            }
                        }
                        if (invitees != null && invitees.Count > 0 &&
                            report.ToString().Contains("test 1: POST accepted") &&
                            report.ToString().Contains("test 2: POST accepted"))
                        {
                            // Same verified Europe project ID as the folders. Owner maps
                            // to ADMIN; all other Tekla roles map to USER.
                            // Send one request only; API errors or ambiguous replies
                            // must never cause automatic duplicate invitations.
                            var members = new JArray(invitees.Select(user => new JObject
                            {
                                ["email"] = user.Email,
                                ["role"] = user.Role == "Owner" ? "ADMIN" : "USER"
                            }));
                            try
                            {
                                using (var body = new StringContent(
                                    members.ToString(), Encoding.UTF8, "application/json"))
                                using (var responseInvite = await http.PostAsync(
                                    EuropeBase + "projects/" + Uri.EscapeDataString(id) +
                                    "/users", body).ConfigureAwait(false))
                                {
                                    string inviteBody = await responseInvite.Content
                                        .ReadAsStringAsync().ConfigureAwait(false);
                                    if (!responseInvite.IsSuccessStatusCode)
                                        report.AppendLine("CONNECT INVITES FAILED: HTTP " +
                                            (int)responseInvite.StatusCode + " " +
                                            inviteBody.Substring(0, Math.Min(inviteBody.Length, 500)));
                                    else
                                        report.AppendLine("CONNECT INVITES SUBMITTED for " +
                                            invitees.Count + " users (HTTP " +
                                            (int)responseInvite.StatusCode + ").");
                                }
                            }
                            catch (Exception ex)
                            {
                                report.AppendLine("CONNECT INVITES UNCERTAIN: " +
                                    ex.GetType().Name + ": " + ex.Message +
                                    ". Verify memberships before retrying.");
                            }
                        }
                        return report.ToString();
                    }
                }
            }
        }

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
                            // The nested licence record may expose its account and
                            // entitlement fields, even when the project does not.
                            JToken licenceObject = detail["license"];
                            if (licenceObject is JObject)
                            {
                                var licenseFields = ((JObject)licenceObject).Properties()
                                    .Select(p => p.Name).ToArray();
                                string licenseAccount = (string)licenceObject["accountId"] ??
                                    (string)licenceObject["account"]?["id"] ??
                                    (string)licenceObject["companyId"] ??
                                    (string)licenceObject["ownerId"];
                                string licenseAccountName = (string)licenceObject["accountName"] ??
                                    (string)licenceObject["account"]?["name"] ??
                                    (string)licenceObject["companyName"];
                                string entitlement = (string)licenceObject["entitlementId"] ??
                                    (string)licenceObject["productId"];
                                var accountSummaries = new System.Collections.Generic.List<string>();
                                var accounts = licenceObject["accounts"] as JArray;
                                if (accounts != null)
                                {
                                    foreach (JToken account in accounts.Take(5))
                                    {
                                        JObject obj = account as JObject;
                                        if (obj == null) continue;
                                        accountSummaries.Add("id=" +
                                            ((string)obj["id"] ?? "(missing)") +
                                            ", name=" + ((string)obj["name"] ??
                                            (string)obj["orgName"] ?? "(missing)"));
                                    }
                                }
                                report.Add("Licence in '" +
                                    ((string)detail["name"] ?? "(unnamed)") +
                                    "': id=" + ((string)licenceObject["id"] ?? "(missing)") +
                                    "; orgName=" + ((string)licenceObject["orgName"] ?? "(missing)") +
                                    "; subscriptionId=" + ((string)licenceObject["subscriptionId"] ?? "(missing)") +
                                    "; sku=" + ((string)licenceObject["sku"] ?? "(missing)") +
                                    "; type=" + ((string)licenceObject["type"] ?? "(missing)") +
                                    "; accounts=" + (accountSummaries.Count == 0
                                        ? "(no account identifiers exposed)"
                                        : string.Join(" | ", accountSummaries)) +
                                    "; ownerId=" + ((string)licenceObject["ownerId"] ?? "(missing)"));
                            }
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
