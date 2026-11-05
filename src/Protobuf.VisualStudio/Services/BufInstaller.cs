using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Protobuf.VisualStudio.Services;

/// <summary>
/// Downloads the latest Buf CLI release from GitHub, verifies it against the release's <c>sha256.txt</c>, and
/// stores it as <c>{installRoot}\v{version}\buf.exe</c>.
/// </summary>
internal static class BufInstaller
{
    private const string LatestReleaseApi = "https://api.github.com/repos/bufbuild/buf/releases/latest";

    public static string AssetName => RuntimeInformation.OSArchitecture == Architecture.Arm64
        ? "buf-Windows-arm64.exe"
        : "buf-Windows-x86_64.exe";

    public static async Task<string> InstallAsync(string installRoot, Action<string> log, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Protobuf-for-Visual-Studio", "0.1"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        log($"Looking up the latest Buf CLI release ({LatestReleaseApi})");
        using var release = JsonDocument.Parse(await http.GetStringAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false));
        var tag = release.RootElement.GetProperty("tag_name").GetString() ?? throw new InvalidOperationException("The release has no tag.");
        string? exeUrl = null;
        string? checksumUrl = null;
        foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == AssetName)
            {
                exeUrl = url;
            }
            else if (name == "sha256.txt")
            {
                checksumUrl = url;
            }
        }

        if (exeUrl is null || checksumUrl is null)
        {
            throw new InvalidOperationException($"Release {tag} has no {AssetName} or sha256.txt asset.");
        }

        var versionDir = Path.Combine(installRoot, "v" + tag.TrimStart('v'));
        var exe = Path.Combine(versionDir, "buf.exe");
        if (File.Exists(exe))
        {
            log($"Buf {tag} is already installed at {exe}.");
            return exe;
        }

        var checksums = await http.GetStringAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
        var expected = checksums
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1] == AssetName)
            .Select(parts => parts[0].ToLowerInvariant())
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"sha256.txt for {tag} has no entry for {AssetName}.");

        Directory.CreateDirectory(versionDir);
        var download = exe + ".download";
        log($"Downloading Buf {tag} from {exeUrl}");
        try
        {
            await DownloadAsync(http, exeUrl, download, progress, cancellationToken).ConfigureAwait(false);

            string actual;
            await using (var stream = File.OpenRead(download))
            {
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Checksum mismatch for {AssetName}: expected {expected}, got {actual}.");
            }

            log("Checksum verified.");
            File.Move(download, exe, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(download);
            }
            catch (IOException)
            {
            }
        }

        log($"Installed Buf {tag} to {exe}");
        return exe;
    }

    private static async Task DownloadAsync(HttpClient http, string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(destination);
        var buffer = new byte[81920];
        long read = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            read += count;
            if (total > 0)
            {
                progress?.Report((double)read / total.Value);
            }
        }
    }
}
