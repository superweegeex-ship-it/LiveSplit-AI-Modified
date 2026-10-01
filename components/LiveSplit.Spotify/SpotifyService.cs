using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LiveSplit.UI.Components
{
    internal sealed class SpotifyService : IDisposable
    {
        private const int CallbackPort = 43821;
        private const string RedirectUri = "http://127.0.0.1:43821/callback/";
        private readonly HttpClient http = new HttpClient();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly object sync = new object();
        private CancellationTokenSource pollingCts;
        private string accessToken, refreshToken, clientId, verifier;
        private DateTime tokenExpiryUtc;
        private SpotifyTrack track;
        private TcpListener connectingListener;
        public event Action Changed;
        public event Action<string> StatusChanged;
        public SpotifyTrack Snapshot { get { lock (sync) return track; } }

        public async Task ConnectAsync(string id)
        {
            clientId = (id ?? "").Trim();
            if (clientId.Length == 0) { Status("Enter a Client ID first"); return; }
            verifier = Base64Url(RandomBytes(64));
            string challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomBytes(24));
            var listener = new TcpListener(IPAddress.Loopback, CallbackPort);
            try
            {
                listener.Start();
                connectingListener = listener;
                var auth = "https://accounts.spotify.com/authorize?" + Form(new Dictionary<string,string> {
                    ["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = RedirectUri,
                    ["scope"] = "user-read-currently-playing user-read-playback-state", ["code_challenge_method"] = "S256",
                    ["code_challenge"] = challenge, ["state"] = state
                });
                Status("Waiting for Spotify…");
                Process.Start(new ProcessStartInfo(auth) { UseShellExecute = true });
                var callback = ReceiveOAuthCallbackAsync(listener);
                if (await Task.WhenAny(callback, Task.Delay(TimeSpan.FromMinutes(2))).ConfigureAwait(false) != callback) {
                    listener.Stop();
                    try { await callback.ConfigureAwait(false); } catch { }
                    throw new TimeoutException("Authorization timed out. Use Connect Spotify to try again.");
                }
                var query = await callback.ConfigureAwait(false);
                if (GetQuery(query, "state") != state) throw new InvalidOperationException("Spotify OAuth state mismatch.");
                var error = GetQuery(query, "error");
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("Spotify authorization: " + error);
                var code = GetQuery(query, "code");
                if (string.IsNullOrEmpty(code)) throw new InvalidOperationException("Spotify did not return an authorization code.");
                await ExchangeCodeAsync(code).ConfigureAwait(false);
                Status("Connected");
                StartPolling();
            }
            catch (Exception ex) { Status("Connection failed: " + ex.Message); }
            finally { connectingListener = null; try { listener.Stop(); } catch { } }
        }

        private static async Task<Dictionary<string,string>> ReceiveOAuthCallbackAsync(TcpListener listener)
        {
            using (var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
            {
                var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(requestLine)) throw new InvalidOperationException("Empty OAuth callback.");
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) throw new InvalidOperationException("Invalid OAuth callback.");
                var uri = new Uri("http://127.0.0.1" + parts[1]);
                var result = ParseQuery(uri.Query);
                var html = "<html><body style='font-family:sans-serif;background:#121212;color:white;padding:30px'><h2>Spotify connected to LiveSplit.</h2><p>You may close this window.</p></body></html>";
                var body = Encoding.UTF8.GetBytes(html);
                var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                return result;
            }
        }

        private async Task ExchangeCodeAsync(string code)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string,string> { ["client_id"] = clientId, ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = RedirectUri, ["code_verifier"] = verifier });
            var r = await http.PostAsync("https://accounts.spotify.com/api/token", form).ConfigureAwait(false);
            var body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Token exchange failed (HTTP " + (int)r.StatusCode + ").");
            var d = json.Deserialize<Dictionary<string,object>>(body);
            accessToken = GetString(d, "access_token"); refreshToken = GetString(d, "refresh_token");
            tokenExpiryUtc = DateTime.UtcNow.AddSeconds(Math.Max(30, GetInt(d, "expires_in", 3600) - 60));
        }

        private async Task RefreshAsync()
        {
            if (string.IsNullOrEmpty(refreshToken)) throw new InvalidOperationException("Spotify refresh token is missing; reconnect Spotify.");
            var form = new FormUrlEncodedContent(new Dictionary<string,string> { ["client_id"] = clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken });
            var r = await http.PostAsync("https://accounts.spotify.com/api/token", form).ConfigureAwait(false);
            var body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Token refresh failed (HTTP " + (int)r.StatusCode + ").");
            var d = json.Deserialize<Dictionary<string,object>>(body);
            accessToken = GetString(d, "access_token");
            var next = GetString(d, "refresh_token"); if (!string.IsNullOrEmpty(next)) refreshToken = next;
            tokenExpiryUtc = DateTime.UtcNow.AddSeconds(Math.Max(30, GetInt(d, "expires_in", 3600) - 60));
        }

        private void StartPolling()
        {
            if (pollingCts != null) { pollingCts.Cancel(); pollingCts.Dispose(); }
            pollingCts = new CancellationTokenSource(); var token = pollingCts.Token;
            Task.Run(async () => {
                while (!token.IsCancellationRequested) {
                    try { await FetchAsync().ConfigureAwait(false); Status("Connected"); }
                    catch (Exception ex) { Status("Spotify: " + ex.Message); }
                    try { await Task.Delay(2000, token).ConfigureAwait(false); } catch (TaskCanceledException) { }
                }
            }, token);
        }

        private async Task FetchAsync()
        {
            if (string.IsNullOrEmpty(accessToken)) return;
            if (DateTime.UtcNow >= tokenExpiryUtc) await RefreshAsync().ConfigureAwait(false);
            using (var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/currently-playing")) {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                var r = await http.SendAsync(req).ConfigureAwait(false);
                if (r.StatusCode == HttpStatusCode.NoContent) { ReplaceTrack(null); return; }
                if (r.StatusCode == HttpStatusCode.Unauthorized) { await RefreshAsync().ConfigureAwait(false); return; }
                var body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException("currently-playing returned HTTP " + (int)r.StatusCode);
                var root = json.Deserialize<Dictionary<string,object>>(body);
                var item = GetDict(root, "item");
                if (item == null) { ReplaceTrack(null); return; }
                var next = new SpotifyTrack { Title = GetString(item,"name"), DurationMs = GetInt(item,"duration_ms"), ProgressMs = GetInt(root,"progress_ms"), IsPlaying = GetBool(root,"is_playing"), ReceivedAtUtc = DateTime.UtcNow };
                var names = new List<string>();
                foreach (var a in Enumerate(GetObject(item,"artists"))) { var ad = a as Dictionary<string,object>; var n = GetString(ad,"name"); if (!string.IsNullOrEmpty(n)) names.Add(n); }
                next.Artist = string.Join(", ", names);
                var album = GetDict(item,"album"); object chosen = null;
                foreach (var img in Enumerate(GetObject(album,"images"))) chosen = img;
                var image = chosen as Dictionary<string,object>; next.ArtworkUrl = GetString(image,"url");
                if (!string.IsNullOrEmpty(next.ArtworkUrl)) try { var b = await http.GetByteArrayAsync(next.ArtworkUrl).ConfigureAwait(false); using (var ms = new MemoryStream(b)) using (var tmp = Image.FromStream(ms)) next.Artwork = new Bitmap(tmp); } catch { }
                ReplaceTrack(next);
            }
        }

        private void ReplaceTrack(SpotifyTrack next) { SpotifyTrack old; lock(sync) { old=track; track=next; } if (old != null) old.Dispose(); var h=Changed; if(h!=null) h(); }
        private void Status(string s) { var h=StatusChanged; if(h!=null) h(s); }
        private static object GetObject(Dictionary<string,object> d,string k) { object v; return d != null && d.TryGetValue(k,out v) ? v : null; }
        private static Dictionary<string,object> GetDict(Dictionary<string,object> d,string k) { return GetObject(d,k) as Dictionary<string,object>; }
        private static IEnumerable Enumerate(object value) { return value as IEnumerable ?? new object[0]; }
        private static byte[] RandomBytes(int n) { var b=new byte[n]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(b); return b; }
        private static string Base64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_'); }
        private static string Form(Dictionary<string,string> d) { var p=new List<string>(); foreach(var kv in d) p.Add(Uri.EscapeDataString(kv.Key)+"="+Uri.EscapeDataString(kv.Value)); return string.Join("&",p); }
        private static Dictionary<string,string> ParseQuery(string q) { var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); foreach(var p in (q??"").TrimStart('?').Split('&')) { if(string.IsNullOrEmpty(p)) continue; var x=p.Split(new[]{'='},2); d[Uri.UnescapeDataString(x[0].Replace('+',' '))]=x.Length>1?Uri.UnescapeDataString(x[1].Replace('+',' ')):""; } return d; }
        private static string GetQuery(Dictionary<string,string> d,string k) { string v; return d.TryGetValue(k,out v)?v:""; }
        private static string GetString(Dictionary<string,object> d,string k) { var v=GetObject(d,k); return v==null?"":Convert.ToString(v); }
        private static int GetInt(Dictionary<string,object> d,string k,int f=0) { var v=GetObject(d,k); if(v!=null) try{return Convert.ToInt32(v);}catch{} return f; }
        private static bool GetBool(Dictionary<string,object> d,string k) { var v=GetObject(d,k); if(v!=null) try{return Convert.ToBoolean(v);}catch{} return false; }
        public void Dispose() { connectingListener?.Stop(); if(pollingCts!=null){pollingCts.Cancel();pollingCts.Dispose();} lock(sync){if(track!=null)track.Dispose();track=null;} http.Dispose(); }
    }
}
