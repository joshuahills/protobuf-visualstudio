using System.Security.Cryptography;
using System.Text.Json;
using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Unit;

public sealed class BufInstallerTests : IDisposable
{
    private const string ReleaseApi = "https://api.test/releases/latest";
    private const string ExeUrl = "https://downloads.test/buf.exe";
    private const string ChecksumUrl = "https://downloads.test/sha256.txt";

    private static readonly byte[] Payload = "MZ fake buf executable"u8.ToArray();

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

    [Fact]
    public async Task Installs_into_a_versioned_folder_after_verifying_the_checksum()
    {
        var handler = Release("v1.80.0", Sha256(Payload));
        var progress = new RecordingProgress();

        var exe = await this.InstallAsync(handler, progress);

        Assert.Equal(Path.Combine(this.temp.Path, "v1.80.0", "buf.exe"), exe);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(exe, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(exe + ".download"));
        Assert.Contains(1.0, progress.Values);
        Assert.Contains(this.log, line => line.Contains("Checksum verified", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Accepts_a_tag_without_the_v_prefix()
    {
        var exe = await this.InstallAsync(Release("1.80.0", Sha256(Payload)));
        Assert.Equal(Path.Combine(this.temp.Path, "v1.80.0", "buf.exe"), exe);
    }

    [Fact]
    public async Task A_checksum_mismatch_fails_and_leaves_nothing_behind()
    {
        var handler = Release("v1.80.0", new string('0', 64));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => this.InstallAsync(handler));

        Assert.Contains("Checksum mismatch", error.Message, StringComparison.Ordinal);
        var versionDir = Path.Combine(this.temp.Path, "v1.80.0");
        Assert.Empty(Directory.EnumerateFiles(versionDir));
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
    public async Task A_checksum_file_without_the_asset_fails_before_downloading()
    {
        var handler = Release("v1.80.0", checksum: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this.InstallAsync(handler));

        Assert.DoesNotContain(ExeUrl, handler.Requests);
    }

    [Fact]
    public async Task An_existing_install_of_the_same_version_is_reused()
    {
        var existing = this.temp.File(@"v1.80.0\buf.exe", "already here");
        var handler = Release("v1.80.0", Sha256(Payload));

        var exe = await this.InstallAsync(handler);

        Assert.Equal(existing, exe);
        Assert.Equal(["https://api.test/releases/latest"], handler.Requests);
        Assert.Equal("already here", await File.ReadAllTextAsync(exe, TestContext.Current.CancellationToken));
    }

    private static FakeHttpHandler Release(string tag, string? checksum)
    {
        var json = JsonSerializer.Serialize(new
        {
            tag_name = tag,
            assets = new[]
            {
                new { name = "buf-Linux-x86_64", browser_download_url = "https://downloads.test/linux" },
                new { name = BufInstaller.AssetName, browser_download_url = ExeUrl },
                new { name = "sha256.txt", browser_download_url = ChecksumUrl },
            },
        });

        var listing = checksum is null
            ? "deadbeef  buf-Linux-x86_64\n"
            : $"deadbeef  buf-Linux-x86_64\n{checksum}  {BufInstaller.AssetName}\n";

        return new FakeHttpHandler()
            .Add(ReleaseApi, json)
            .Add(ChecksumUrl, listing)
            .Add(ExeUrl, Payload);
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private Task<string> InstallAsync(FakeHttpHandler handler, IProgress<double>? progress = null) =>
        BufInstaller.InstallAsync(this.temp.Path, this.log.Add, progress, handler, ReleaseApi, TestContext.Current.CancellationToken);

    /// <summary>Records synchronously, unlike <see cref="Progress{T}"/> which posts to the thread pool.</summary>
    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => this.Values.Add(value);
    }
}
