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
    internal static class TrimbleLicenseDiscovery
    {
        // Read-only exploration of the signed-in user's licences.
        // /users/me is documented; the other two routes are probes and may not exist.
        public static async Task<string> InspectAsync(string accessToken)
        {
            string[] routes =
            {
                "https://app.connect.trimble.com/tc/api/2.0/users/me",
                "https://app.connect.trimble.com/tc/api/2.0/users/me/licenses",
                "https://app.connect.trimble.com/tc/api/2.0/licenses",
                "https://app21.connect.trimble.com/tc/api/2.0/users/me"
            };

            var output = new StringBuilder();
            output.AppendLine("Licence discovery (read-only):");
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
                foreach (string route in routes)
                {
                    try
                    {
                        using (HttpResponseMessage response =
                            await http.GetAsync(route).ConfigureAwait(false))
                        {
                            output.AppendLine(new Uri(route).Host +
                                new Uri(route).AbsolutePath.Replace("/tc/api/2.0", "") +
                                ": HTTP " + (int)response.StatusCode);
                            if (!response.IsSuccessStatusCode) continue;

                            string body = await response.Content.ReadAsStringAsync()
                                .ConfigureAwait(false);
                            JToken root = JToken.Parse(body);
                            List<JObject> candidates = new List<JObject>();
                            FindCandidates(root, candidates, 0);
                            if (candidates.Count == 0)
                            {
                                output.AppendLine("  No identifiable licence records in this response.");
                                continue;
                            }
                            foreach (JObject item in candidates.Take(12))
                                output.AppendLine("  " + Summarize(item));
                            if (candidates.Count > 12)
                                output.AppendLine("  (Additional entries omitted)");
                        }
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine(new Uri(route).AbsolutePath + ": " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            output.AppendLine("No project or folders were created. " +
                "Only a verified Severfield Business Premium licence ID should be used for creation.");
            return output.ToString();
        }

        private static void FindCandidates(JToken token, List<JObject> result, int depth)
        {
            if (token == null || depth > 8 || result.Count >= 30) return;
            var obj = token as JObject;
            if (obj != null)
            {
                bool hasLicenseKey = obj.Properties().Any(p =>
                    p.Name.IndexOf("licen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.Name.IndexOf("entitle", StringComparison.OrdinalIgnoreCase) >= 0);
                if (hasLicenseKey && obj.Properties().Any(p =>
                    p.Value.Type == JTokenType.String || p.Value.Type == JTokenType.Integer))
                    result.Add(obj);
                foreach (JProperty prop in obj.Properties())
                    if (prop.Value.Type == JTokenType.Array || prop.Value.Type == JTokenType.Object)
                        FindCandidates(prop.Value, result, depth + 1);
            }
            else if (token is JArray)
            {
                foreach (JToken child in (JArray)token)
                    FindCandidates(child, result, depth + 1);
            }
        }

        private static string Summarize(JObject obj)
        {
            var values = new List<string>();
            foreach (JProperty p in obj.Properties())
            {
                if (p.Value.Type != JTokenType.String &&
                    p.Value.Type != JTokenType.Integer) continue;
                string name = p.Name.ToLowerInvariant();
                if (name == "id" || name.Contains("license") ||
                    name.Contains("entitle") || name.Contains("account") ||
                    name.Contains("company") || name == "name" ||
                    name == "type" || name == "product")
                {
                    string value = p.Value.ToString();
                    if (value.Length > 120) value = value.Substring(0, 120) + "...";
                    values.Add(p.Name + "=" + value);
                }
            }
            return values.Count == 0 ? "(Licence-like record; no safe scalar identifiers)"
                : string.Join(", ", values);
        }
    }
}
