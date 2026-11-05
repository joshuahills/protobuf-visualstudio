using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Protobuf.VisualStudio.Security;

namespace Protobuf.VisualStudio.Services;

/// <summary>
/// Downloads a Buf CLI release from GitHub (the latest, or a pinned version) and stores it as
/// <c>{installRoot}\v{version}\buf.exe</c>. Before anything is trusted, the release's <c>sha256.txt</c> must carry a
/// valid minisign signature from Buf's release key; the binary must then match its entry in that file.
/// </summary>
internal static class BufInstaller
{
    public const string LatestReleaseApi = "https://api.github.com/repos/bufbuild/buf/releases/latest";

    public static string AssetName => RuntimeInformation.OSArchitecture == Architecture.Arm64
        ? "buf-Windows-arm64.exe"
        : "buf-Windows-x86_64.exe";

    /// <param name="version">A version such as <c>1.75.0</c> to install that release, or null for the latest.</param>
    public static Task<string> InstallAsync(string installRoot, Action<string> log, IProgress<double>? progress, CancellationToken cancellationToken, string? version = null) =>
        InstallAsync(installRoot, log, progress, handler: null, ReleaseApi(version), Minisign.BufPublicKey, cancellationToken);

    /// <summary>The GitHub API URL for <paramref name="version"/> (with or without a leading v), or the latest release.</summary>
    public static string ReleaseApi(string? version) => string.IsNullOrWhiteSpace(version)
        ? LatestReleaseApi
        : $"https://api.github.com/repos/bufbuild/buf/releases/tags/v{version.Trim().TrimStart('v', 'V')}";

    /// <summary>
    /// Test seam: <paramref name="handler"/> replaces the network, <paramref name="releaseApi"/> the release URL and
    /// <paramref name="publicKey"/> Buf's signing key.
    /// </summary>
    internal static async Task<string> InstallAsync(string installRoot, Action<string> log, IProgress<double>? progress, HttpMessageHandler? handler, string releaseApi, string publicKey, CancellationToken cancellationToken)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Protobuf-for-Visual-Studio", "0.1"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        log($"Looking up the Buf CLI release ({releaseApi})");
        using var release = JsonDocument.Parse(await http.GetStringAsync(releaseApi, cancellationToken).ConfigureAwait(false));
        var tag = release.RootElement.GetProperty("tag_name").GetString() ?? throw new InvalidOperationException("The release has no tag.");
        string? exeUrl = null;
        string? checksumUrl = null;
        string? signatureUrl = null;
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
            else if (name == "sha256.txt.minisig")
            {
                signatureUrl = url;
            }
        }

        if (exeUrl is null || checksumUrl is null || signatureUrl is null)
        {
            throw new InvalidOperationException($"Release {tag} is missing {AssetName}, sha256.txt or sha256.txt.minisig, so it can't be verified.");
        }

        var versionDir = Path.Combine(installRoot, "v" + tag.TrimStart('v'));
        var exe = Path.Combine(versionDir, "buf.exe");
        if (File.Exists(exe))
        {
            log($"Buf {tag} is already installed at {exe}.");
            return exe;
        }

        // Verify the exact bytes that were signed, before decoding them.
        var checksumBytes = await http.GetByteArrayAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
        var signature = await http.GetStringAsync(signatureUrl, cancellationToken).ConfigureAwait(false);
        try
        {
            Minisign.Verify(publicKey, signature, checksumBytes);
        }
        catch (MinisignException ex)
        {
            throw new InvalidOperationException($"sha256.txt for {tag} failed its signature check ({ex.Message}). Nothing was installed.", ex);
        }

        log("Signature of sha256.txt verified with Buf's release key.");
        var checksums = Encoding.UTF8.GetString(checksumBytes);
        var expected = FindChecksum(checksums, AssetName)
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

    /// <summary>Returns the lowercase SHA-256 for <paramref name="assetName"/> from a <c>sha256sum</c>-style listing.</summary>
    internal static string? FindChecksum(string checksums, string assetName) => checksums
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length == 2 && parts[1].TrimStart('*') == assetName)
        .Select(parts => parts[0].ToLowerInvariant())
        .FirstOrDefault();

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
