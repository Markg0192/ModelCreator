using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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
        private const string Redirect = "http://localhost";
        private const string ListenerPrefix = "http://localhost/";
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
            // A normal TCP listener avoids Windows HTTP.sys URL ACL permissions.
            // Port 80 matches the registered http://localhost callback.
            var listener = new TcpListener(IPAddress.IPv6Any, 80);
            try
            {
                listener.Server.DualMode = true; // accepts localhost over IPv4 or IPv6
                try
                {
                    listener.Start();
                }
                catch (SocketException ex)
                {
                    throw new InvalidOperationException(
                        "Cannot listen for Trimble sign-in on localhost port 80. " +
                        "Another application may be using that port, or Windows may be blocking it. " +
                        "Details: " + ex.Message, ex);
                }

                string authorize = BaseUrl + "/oauth/authorize?response_type=code" +
                    "&client_id=" + Uri.EscapeDataString(ClientId) +
                    "&redirect_uri=" + Uri.EscapeDataString(Redirect) +
                    "&scope=" + Uri.EscapeDataString("openid Severfield_ModelCreator") +
                    "&state=" + Uri.EscapeDataString(state) +
                    "&code_challenge_method=S256&code_challenge=" + Uri.EscapeDataString(challenge);

                Process.Start(new ProcessStartInfo(authorize) { UseShellExecute = true });
                Task<TcpClient> callback = listener.AcceptTcpClientAsync();
                if (await Task.WhenAny(callback, Task.Delay(TimeSpan.FromMinutes(3)))
                    .ConfigureAwait(false) != callback)
                    throw new TimeoutException("Trimble sign-in timed out.");

                string returnedState;
                string code;
                string oauthError;
                using (TcpClient connection = await callback.ConfigureAwait(false))
                using (NetworkStream stream = connection.GetStream())
                {
                    stream.ReadTimeout = 10000;
                    var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    string requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(requestLine) ||
                        !requestLine.StartsWith("GET /", StringComparison.Ordinal))
                        throw new InvalidOperationException("Unexpected Trimble callback request.");

                    string[] segments = requestLine.Split(' ');
                    if (segments.Length < 3)
                        throw new InvalidOperationException("Invalid Trimble callback request.");

                    var callbackUri = new Uri("http://localhost" + segments[1]);
                    var parameters = ParseQuery(callbackUri.Query);
                    returnedState = parameters.ContainsKey("state") ? parameters["state"] : null;
                    code = parameters.ContainsKey("code") ? parameters["code"] : null;
                    oauthError = parameters.ContainsKey("error") ? parameters["error"] : null;

                    string html = "<html><body>Trimble sign-in received. You can return to ModelCreator.</body></html>";
                    byte[] body = Encoding.UTF8.GetBytes(html);
                    string headers = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                        "Content-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
                    byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
                    await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }

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
            finally
            {
                listener.Stop();
            }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in query.TrimStart('?').Split('&'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                string[] pair = part.Split(new[] { '=' }, 2);
                string key = Uri.UnescapeDataString(pair[0].Replace("+", " "));
                string value = pair.Length == 2
                    ? Uri.UnescapeDataString(pair[1].Replace("+", " ")) : "";
                result[key] = value;
            }
            return result;
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
