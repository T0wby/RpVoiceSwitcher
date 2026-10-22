using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RpVoiceSwitcher;

/// <summary>
/// Sucht über GitHub-Releases nach einer neuen Version. Ein Release heißt z. B. "v1.3.0" und
/// enthält den Installer "RpVoiceSwitcher-Setup-1.3.0.exe" als Anhang.
/// </summary>
public static class UpdateChecker
{
    /// <summary>GitHub-Repository mit den Releases (öffentlich, damit die Prüfung ohne Anmeldung geht).</summary>
    public const string Repository = "T0wby/RpVoiceSwitcher";

    public sealed record Release(Version Version, string Tag, string PageUrl, string? InstallerUrl, string Notes);

    public enum State { UpToDate, UpdateAvailable, NoChannel, Failed }

    public sealed record Result(State State, Release? Release = null, string? Error = null);

    public static Version CurrentVersion
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];
            return TryParseVersion(info, out var v) ? v : asm.GetName().Version ?? new Version(0, 0);
        }
    }

    public static string CurrentVersionText => Format(CurrentVersion);

    public static string Format(Version v) => v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}.0";

    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimStart('v', 'V');
        int dash = t.IndexOf('-');
        if (dash > 0) t = t[..dash]; // "1.3.0-beta" → 1.3.0
        if (!Version.TryParse(t, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"RpVoiceSwitcher/{CurrentVersionText}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static async Task<Result> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = CreateClient();
            using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return new Result(State.NoChannel);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out var latest)) return new Result(State.Failed, Error: $"Unbekannte Versionsangabe „{tag}“");

            string? installer = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                var urls = assets.EnumerateArray()
                    .Select(a => a.GetProperty("browser_download_url").GetString())
                    .Where(u => u != null && u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                installer = urls.FirstOrDefault(u => u!.Contains("Setup", StringComparison.OrdinalIgnoreCase)) ?? urls.FirstOrDefault();
            }

            var release = new Release(latest, tag,
                root.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "",
                installer,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
            return new Result(latest > CurrentVersion ? State.UpdateAvailable : State.UpToDate, release);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Result(State.Failed, Error: "Zeitüberschreitung");
        }
        catch (Exception ex)
        {
            return new Result(State.Failed, Error: ex.Message);
        }
    }

    /// <summary>Lädt den Installer in den Temp-Ordner und gibt den Pfad zurück.</summary>
    public static async Task<string> DownloadAsync(string url, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var http = CreateClient();
        http.Timeout = TimeSpan.FromMinutes(10);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        string name = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = "RpVoiceSwitcher-Setup.exe";
        string dir = Path.Combine(Path.GetTempPath(), "RpVoiceSwitcher-Update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);

        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(path))
        {
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total.Value);
            }
        }
        return path;
    }
}
