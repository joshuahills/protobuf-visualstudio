using System.Text.Json;
using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Unit;

/// <summary>
/// The installer against a fake GitHub. Checksum files are signed with a throwaway minisign key
/// (TestData/minisign-test, see its README) so the real signature path runs.
/// </summary>
public sealed class BufInstallerTests : IDisposable
{
    private const string ReleaseApi = "https://api.test/releases/latest";
    private const string ExeUrl = "https://downloads.test/buf.exe";
    private const string ChecksumUrl = "https://downloads.test/sha256.txt";
    private const string SignatureUrl = "https://downloads.test/sha256.txt.minisig";

    private static readonly byte[] Payload = "MZ fake buf executable"u8.ToArray();

    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "minisign-test");

    private static readonly string TestKey = File.ReadAllText(Path.Combine(Fixtures, "test-key.pub")).Trim();

    private readonly TempDirectory temp = new();
    private readonly List<string> log = [];

    public void Dispose() => this.temp.Dispose();

    [Theory]
    [InlineData("abc123  buf-Windows-x86_64.exe", "abc123")]
    [InlineData("ABC123 *buf-Windows-x86_64.exe\r", "abc123")]
    [InlineData("111  buf-Windows-arm64.exe\n222  buf-Windows-x86_64.exe\n", "222")]
    public void FindChecksum_matches_the_asset_name(string listing, string expected)
    {
        Assert.Equal(expected, BufInstaller.FindChecksum(listing, "buf-Windows-x86_64.exe"));
    }

    [Fact]
    public void FindChecksum_does_not_match_a_longer_name()
    {
        Assert.Null(BufInstaller.FindChecksum("abc  buf-Windows-x86_64.exe.zip", "buf-Windows-x86_64.exe"));
    }

    [Theory]
    [InlineData(null, "https://api.github.com/repos/bufbuild/buf/releases/latest")]
    [InlineData("", "https://api.github.com/repos/bufbuild/buf/releases/latest")]
    [InlineData("1.75.0", "https://api.github.com/repos/bufbuild/buf/releases/tags/v1.75.0")]
    [InlineData(" v1.60.1 ", "https://api.github.com/repos/bufbuild/buf/releases/tags/v1.60.1")]
    public void Release_urls_for_latest_and_pinned_versions(string? version, string expected)
    {
        Assert.Equal(expected, BufInstaller.ReleaseApi(version));
    }

    [Fact]
    public async Task Installs_into_a_versioned_folder_after_verifying_signature_and_checksum()
    {
        var progress = new RecordingProgress();

        var exe = await this.InstallAsync(Release("v1.80.0", "ok"), progress);

        Assert.Equal(Path.Combine(this.temp.Path, "v1.80.0", "buf.exe"), exe);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(exe + ".download"));
        Assert.Contains(1.0, progress.Values);
        Assert.Contains(this.log, line => line.Contains("Signature of sha256.txt verified", StringComparison.Ordinal));
        Assert.Contains(this.log, line => line.Contains("Checksum verified", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Accepts_a_tag_without_the_v_prefix()
    {
        var exe = await this.InstallAsync(Release("1.80.0", "ok"));
        Assert.Equal(Path.Combine(this.temp.Path, "v1.80.0", "buf.exe"), exe);
    }

    [Fact]
    public async Task A_tampered_checksum_file_is_rejected_before_downloading()
    {
        var handler = Release("v1.80.0", "ok");
        var forged = File.ReadAllBytes(Path.Combine(Fixtures, "ok.sha256.txt"));
        forged[0] ^= 0x01;
        handler.Add(ChecksumUrl, forged);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => this.InstallAsync(handler));

        Assert.Contains("signature check", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ExeUrl, handler.Requests);
        Assert.False(Directory.Exists(Path.Combine(this.temp.Path, "v1.80.0")));
    }

    [Fact]
    public async Task A_checksum_file_signed_by_another_key_is_rejected()
    {
        // The real Buf key cannot have signed the test fixture.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BufInstaller.InstallAsync(this.temp.Path, this.log.Add, null, Release("v1.80.0", "ok"), ReleaseApi, Protobuf.VisualStudio.Security.Minisign.BufPublicKey, TestContext.Current.CancellationToken));

        Assert.Contains("different key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_release_without_a_signature_is_not_installed()
    {
        var handler = Release("v1.80.0", "ok", includeSignature: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => this.InstallAsync(handler));

        Assert.Contains("sha256.txt.minisig", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ExeUrl, handler.Requests);
    }

    [Fact]
    public async Task A_checksum_mismatch_fails_and_leaves_nothing_behind()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => this.InstallAsync(Release("v1.80.0", "mismatch")));

        Assert.Contains("Checksum mismatch", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(this.temp.Path, "v1.80.0")));
    }

    [Fact]
    public async Task A_release_without_the_windows_asset_fails()
    {
        var json = JsonSerializer.Serialize(new { tag_name = "v1.80.0", assets = new[] { new { name = "sha256.txt", browser_download_url = ChecksumUrl } } });
        var handler = new FakeHttpHandler().Add(ReleaseApi, json);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => this.InstallAsync(handler));

        Assert.Contains(BufInstaller.AssetName, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signed_checksum_file_without_the_asset_fails_before_downloading()
    {
        var handler = Release("v1.80.0", "missing");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => this.InstallAsync(handler));

        Assert.Contains("no entry", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ExeUrl, handler.Requests);
    }

    [Fact]
    public async Task An_existing_install_of_the_same_version_is_reused()
    {
        var existing = this.temp.File(@"v1.80.0\buf.exe", "already here");
        var handler = Release("v1.80.0", "ok");

        var exe = await this.InstallAsync(handler);

        Assert.Equal(existing, exe);
        Assert.Equal([ReleaseApi], handler.Requests);
        Assert.Equal("already here", await File.ReadAllTextAsync(exe, TestContext.Current.CancellationToken));
    }

    private static FakeHttpHandler Release(string tag, string fixture, bool includeSignature = true)
    {
        List<object> assets =
        [
            new { name = "buf-Linux-x86_64", browser_download_url = "https://downloads.test/linux" },
            new { name = BufInstaller.AssetName, browser_download_url = ExeUrl },
            new { name = "sha256.txt", browser_download_url = ChecksumUrl },
        ];
        if (includeSignature)
        {
            assets.Add(new { name = "sha256.txt.minisig", browser_download_url = SignatureUrl });
        }

        return new FakeHttpHandler()
            .Add(ReleaseApi, JsonSerializer.Serialize(new { tag_name = tag, assets }))
            .Add(ChecksumUrl, File.ReadAllBytes(Path.Combine(Fixtures, $"{fixture}.sha256.txt")))
            .Add(SignatureUrl, File.ReadAllBytes(Path.Combine(Fixtures, $"{fixture}.sha256.txt.minisig")))
            .Add(ExeUrl, Payload);
    }

    private Task<string> InstallAsync(FakeHttpHandler handler, IProgress<double>? progress = null) =>
        BufInstaller.InstallAsync(this.temp.Path, this.log.Add, progress, handler, ReleaseApi, TestKey, TestContext.Current.CancellationToken);

    /// <summary>Records synchronously, unlike <see cref="Progress{T}"/> which posts to the thread pool.</summary>
    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => this.Values.Add(value);
    }
}
