using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ModelCreator.Services
{
    // Interactive OAuth: the desktop program never stores or distributes a client secret.
    internal static class TrimbleIdentity
    {
        private const string ClientId = "76f248a9-469b-47c4-81ec-b2cfb1acfe8a";
        private const string Redirect = "http://localhost/";
        private const string BaseUrl = "https://id.trimble.com";

        internal static async Task<string> LoginAsync()
        {
            string verifier = Base64Url(RandomBytes(48));
            string state = Base64Url(RandomBytes(24));
            string challenge;
            using (var sha = SHA256.Create())
                challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

            // Registered callback is http://localhost. Avoid changing the redirect
            // port/path unless Trimble confirms that URL is registered.
            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(Redirect);
                listener.Start();
                string authorize = BaseUrl + "/oauth/authorize?response_type=code" +
                    "&client_id=" + Uri.EscapeDataString(ClientId) +
                    "&redirect_uri=" + Uri.EscapeDataString(Redirect) +
                    "&scope=" + Uri.EscapeDataString("openid Severfield_ModelCreator") +
                    "&state=" + Uri.EscapeDataString(state) +
                    "&code_challenge_method=S256&code_challenge=" + Uri.EscapeDataString(challenge);

                Process.Start(new ProcessStartInfo(authorize) { UseShellExecute = true });
                var callback = listener.GetContextAsync();
                if (await Task.WhenAny(callback, Task.Delay(TimeSpan.FromMinutes(3)))
                    .ConfigureAwait(false) != callback)
                    throw new TimeoutException("Trimble sign-in timed out.");

                HttpListenerContext context = await callback.ConfigureAwait(false);
                string returnedState = context.Request.QueryString["state"];
                string code = context.Request.QueryString["code"];
                string oauthError = context.Request.QueryString["error"];
                const string page = "<html><body>Trimble login received. Return to ModelCreator.</body></html>";
                byte[] bytes = Encoding.UTF8.GetBytes(page);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                context.Response.Close();

                if (!string.Equals(state, returnedState, StringComparison.Ordinal))
                    throw new InvalidOperationException("Trimble sign-in state verification failed.");
                if (!string.IsNullOrEmpty(oauthError))
                    throw new InvalidOperationException("Trimble sign-in error: " + oauthError);
                if (string.IsNullOrWhiteSpace(code))
                    throw new InvalidOperationException("Trimble did not return an authorization code.");

                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                using (var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "client_id", ClientId },
                    { "redirect_uri", Redirect },
                    { "code_verifier", verifier }
                }))
                using (HttpResponseMessage response = await http.PostAsync(BaseUrl + "/oauth/token", form)
                    .ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Trimble token request failed (HTTP " +
                            (int)response.StatusCode + ").");
                    string token = (string)JObject.Parse(body)["access_token"];
                    if (string.IsNullOrWhiteSpace(token))
                        throw new InvalidOperationException("Trimble response did not contain an access token.");
                    return token;
                }
            }
        }

        private static byte[] RandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            using (var generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            return bytes;
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
