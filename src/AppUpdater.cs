using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WheekKeys
{
    internal sealed class AppUpdater
    {
        private const string DefaultUpdateUrl = "https://api.github.com/repos/gwtt/guinea-pig-keyboard/releases/latest";
        private const int ManifestLimit = 1024 * 1024, DownloadLimit = 100 * 1024 * 1024;
        private const int MinimumDownloadSize = 64 * 1024;
        private static readonly Regex SemVer = new Regex(
            @"\A[vV]?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z");
        private static readonly HttpClient Client = CreateClient();
        private readonly string directory;
        private readonly string currentVersion;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private long? lastSuccessfulCheck;

        internal sealed class Release
        {
            internal string Version;
            internal Uri Download;
            internal string Sha256;
            internal string Notes;
        }

        internal Release Available { get; private set; }
        internal bool Busy { get; private set; }
        internal string Status { get; private set; }
        internal event Action Changed;

        internal AppUpdater(string directory)
        {
            this.directory = directory;
            currentVersion = typeof(AppUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        }

        internal async Task CheckAsync()
        {
            if (Busy || lifetime.IsCancellationRequested) return;
            long now = Stopwatch.GetTimestamp();
            if (lastSuccessfulCheck.HasValue && (double)(now - lastSuccessfulCheck.Value) / Stopwatch.Frequency < 300) return;
            Busy = true;
            Notify();
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    Uri source = HttpsUri(UpdateUrl());
                    if (source == null) throw new InvalidDataException("更新源需要有效的 HTTPS 地址。");
                    string json = await ReadManifestAsync(source, timeout.Token);
                    Release latest = ParseRelease(json);
                    timeout.Token.ThrowIfCancellationRequested();
                    Available = IsNewer(latest.Version, currentVersion) ? latest : null;
                    if (Available == null) Status = null;
                    lastSuccessfulCheck = Stopwatch.GetTimestamp();
                }
            }
            catch (Exception e)
            {
                if (!ExpectedError(e)) throw;
            }
            finally { Busy = false; Notify(); }
        }

        internal async Task<bool> InstallAsync()
        {
            Release release = Available;
            if (release == null || Busy || lifetime.IsCancellationRequested) return false;
            Busy = true;
            Status = "正在下载更新…";
            Notify();
            string updates = Path.Combine(directory, "updates");
            string pending = Path.Combine(updates, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                string downloaded = Path.Combine(updates, "WheekKeys.update.exe");
                Directory.CreateDirectory(updates);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(10));
                    await DownloadAsync(release, pending, timeout.Token);
                    timeout.Token.ThrowIfCancellationRequested();
                }
                if (File.Exists(downloaded)) File.Replace(pending, downloaded, null);
                else File.Move(pending, downloaded);
                LaunchInstaller(updates, downloaded);
                return true;
            }
            catch (Exception e)
            {
                if (!ExpectedError(e)) throw;
                Status = "更新失败，请稍后重试。";
                return false;
            }
            finally
            {
                TryDelete(pending);
                Busy = false;
                Notify();
            }
        }

        internal void Cancel() { lifetime.Cancel(); }

        private void Notify()
        {
            if (!lifetime.IsCancellationRequested && Changed != null) Changed();
        }

        private string UpdateUrl()
        {
            string value = Environment.GetEnvironmentVariable("WHEEKKEYS_UPDATE_URL");
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            try
            {
                string path = Path.Combine(directory, "update-url.txt");
                if (File.Exists(path))
                    foreach (string line in File.ReadLines(path, Encoding.UTF8))
                        if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return DefaultUpdateUrl;
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WheekKeys-Updater");
            return client;
        }

        private static async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
        {
            for (int redirects = 0; redirects < 6; redirects++)
            {
                HttpResponseMessage response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status < 300 || status >= 400) return response;
                using (response)
                {
                    Uri next = response.Headers.Location;
                    Uri resolved;
                    if (next == null || !Uri.TryCreate(uri, next, out resolved) || HttpsUri(resolved.AbsoluteUri) == null)
                        throw new InvalidDataException("更新地址重定向无效。");
                    uri = resolved;
                }
            }
            throw new InvalidDataException("更新地址重定向次数过多。");
        }

        private static async Task<string> ReadManifestAsync(Uri uri, CancellationToken token)
        {
            using (var data = new MemoryStream())
            {
                await TransferAsync(uri, data, ManifestLimit, token).ConfigureAwait(false);
                return new UTF8Encoding(false, true).GetString(data.ToArray()).TrimStart('\uFEFF');
            }
        }

        private static async Task DownloadAsync(Release release, string path, CancellationToken token)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, true))
            {
                await TransferAsync(release.Download, file, DownloadLimit, token).ConfigureAwait(false);
                ValidateDownload(file, release.Sha256);
            }
        }

        private static void ValidateDownload(Stream file, string digest)
        {
            file.Position = 0;
            if (file.Length <= MinimumDownloadSize || file.ReadByte() != 'M' || file.ReadByte() != 'Z')
                throw new InvalidDataException("下载的文件不是有效的更新程序。");
            if (digest == null) return;
            file.Position = 0;
            using (SHA256 sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新程序的 SHA-256 校验失败。");
            }
        }

        private static async Task TransferAsync(Uri uri, Stream output, int limit, CancellationToken token)
        {
            using (HttpResponseMessage response = await GetAsync(uri, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long? length = response.Content.Headers.ContentLength;
                if (length > limit) throw new InvalidDataException("更新文件超过大小限制。");
                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (token.Register(response.Dispose))
                {
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                    {
                        if (output.Length + count > limit) throw new InvalidDataException("更新文件超过大小限制。");
                        await output.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
                    }
                }
                token.ThrowIfCancellationRequested();
                if (length.HasValue && output.Length != length.Value) throw new InvalidDataException("更新文件下载不完整。");
            }
        }

        internal static Release ParseRelease(string json)
        {
            var fields = new JavaScriptSerializer { MaxJsonLength = ManifestLimit, RecursionLimit = 24 }.DeserializeObject(json)
                as Dictionary<string, object>;
            string version = StringValue(fields, "version") ?? StringValue(fields, "tag_name");
            if (!MatchVersion(version).Success) throw new InvalidDataException("更新版本号无效。");
            string url = StringValue(fields, "url");
            string digest = StringValue(fields, "sha256");
            string notes = StringValue(fields, "notes");
            if (StringValue(fields, "version") == null)
            {
                url = null;
                notes = StringValue(fields, "body");
                object assets;
                var list = fields.TryGetValue("assets", out assets) ? assets as object[] : null;
                if (list != null)
                    foreach (object asset in list)
                    {
                        var entry = asset as Dictionary<string, object>;
                        Uri candidate = HttpsUri(StringValue(entry, "browser_download_url"));
                        if (candidate == null || !candidate.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        url = candidate.AbsoluteUri;
                        string assetDigest = StringValue(entry, "digest");
                        if (assetDigest != null && assetDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                            digest = assetDigest.Substring(7);
                        break;
                    }
            }
            Uri download = HttpsUri(url);
            if (download == null) throw new InvalidDataException("更新中缺少有效的 HTTPS 下载地址。");
            if (digest != null && !Regex.IsMatch(digest, @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("更新中的 SHA-256 格式无效。");
            return new Release { Version = version.TrimStart('v', 'V'), Download = download, Sha256 = digest, Notes = notes };
        }

        private static string StringValue(Dictionary<string, object> fields, string key)
        {
            object result;
            if (fields == null || !fields.TryGetValue(key, out result) || result == null) return null;
            string value = result as string;
            if (value == null) throw new InvalidDataException("更新清单中的 " + key + " 无效。");
            return value;
        }

        private static Uri HttpsUri(string text)
        {
            Uri uri;
            return Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
                !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
        }

        internal static bool IsNewer(string candidate, string current)
        {
            Match next = MatchVersion(candidate), installed = MatchVersion(current);
            if (!next.Success || !installed.Success) return false;
            for (int i = 1; i <= 3; i++)
            {
                int comparison = CompareNumber(next.Groups[i].Value, installed.Groups[i].Value);
                if (comparison != 0) return comparison > 0;
            }
            string after = next.Groups[4].Value, before = installed.Groups[4].Value;
            if (after.Length == 0 || before.Length == 0) return after.Length == 0 && before.Length != 0;
            string[] a = after.Split('.'), b = before.Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                bool an = Numeric(a[i]), bn = Numeric(b[i]);
                int comparison = an && bn ? CompareNumber(a[i], b[i]) :
                    an == bn ? string.CompareOrdinal(a[i], b[i]) : an ? -1 : 1;
                if (comparison != 0) return comparison > 0;
            }
            return a.Length > b.Length;
        }

        private static Match MatchVersion(string version)
        {
            Match match = SemVer.Match(version ?? "");
            if (match.Success)
                foreach (string part in match.Groups[4].Value.Split('.'))
                    if (part.Length > 1 && part[0] == '0' && Numeric(part)) return Match.Empty;
            return match;
        }

        private static bool Numeric(string value)
        {
            foreach (char c in value) if (c < '0' || c > '9') return false;
            return value.Length != 0;
        }

        private static int CompareNumber(string a, string b)
        {
            int size = a.Length.CompareTo(b.Length);
            return size != 0 ? size : string.CompareOrdinal(a, b);
        }

        private static bool ExpectedError(Exception e)
        {
            return e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException ||
                e is InvalidOperationException || e is HttpRequestException || e is OperationCanceledException ||
                e is WebException || e is System.ComponentModel.Win32Exception || e is CryptographicException;
        }

        private static void LaunchInstaller(string updates, string downloaded)
        {
            string exe = Assembly.GetEntryAssembly().Location;
            string id = Guid.NewGuid().ToString("N");
            string script = Path.Combine(updates, "apply-" + id + ".cmd");
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/d /v:off /s /c \"\"%WHEEK_UPDATE_SCRIPT%\"\"",
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = updates
            };
            start.EnvironmentVariables["WHEEK_UPDATE_PID"] = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            start.EnvironmentVariables["WHEEK_UPDATE_EXE"] = exe;
            start.EnvironmentVariables["WHEEK_UPDATE_EXE_DIR"] = Path.GetDirectoryName(exe);
            start.EnvironmentVariables["WHEEK_UPDATE_PAYLOAD"] = downloaded;
            start.EnvironmentVariables["WHEEK_UPDATE_SCRIPT"] = script;
            start.EnvironmentVariables["WHEEK_UPDATE_SWAP"] = exe + "." + id + ".tmp";
            try
            {
                File.WriteAllText(script, InstallerScript, Encoding.ASCII);
                using (Process process = Process.Start(start))
                    if (process == null) throw new InvalidOperationException("更新程序未启动。");
            }
            catch { TryDelete(script); throw; }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private const string InstallerScript = @"@echo off
setlocal DisableDelayedExpansion
""%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"" -NoProfile -NonInteractive -Command ""try { $running = [Diagnostics.Process]::GetProcessById([int]$env:WHEEK_UPDATE_PID) } catch [ArgumentException] { exit 0 }; if (-not $running.WaitForExit(60000)) { exit 1 }""
if errorlevel 1 goto cleanup
copy /b /y ""%WHEEK_UPDATE_PAYLOAD%"" ""%WHEEK_UPDATE_SWAP%"" >nul || goto restart
copy /b /y ""%WHEEK_UPDATE_EXE%"" ""%WHEEK_UPDATE_EXE%.bak"" >nul || goto restart
move /y ""%WHEEK_UPDATE_SWAP%"" ""%WHEEK_UPDATE_EXE%"" >nul || goto restart
start """" /d ""%WHEEK_UPDATE_EXE_DIR%"" ""%WHEEK_UPDATE_EXE%""
if not errorlevel 1 goto cleanup
move /y ""%WHEEK_UPDATE_EXE%.bak"" ""%WHEEK_UPDATE_EXE%"" >nul
:restart
start """" /d ""%WHEEK_UPDATE_EXE_DIR%"" ""%WHEEK_UPDATE_EXE%""
:cleanup
if exist ""%WHEEK_UPDATE_SWAP%"" del /q ""%WHEEK_UPDATE_SWAP%""
del /q ""%WHEEK_UPDATE_SCRIPT%""
";
    }
}
