using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Integration;

/// <summary>
/// The real installer against github.com with Buf's real signing key: downloads about 50 MB, so it only runs when
/// PROTOBUF_VS_NETWORK_TESTS=1.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RealInstallerTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    [Fact]
    public async Task Installs_a_pinned_release_after_verifying_its_signature()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PROTOBUF_VS_NETWORK_TESTS") == "1", "Set PROTOBUF_VS_NETWORK_TESTS=1 to download a real buf release.");

        List<string> log = [];
        var exe = await BufInstaller.InstallAsync(this.temp.Path, log.Add, progress: null, TestContext.Current.CancellationToken, version: "1.75.0");

        Assert.Equal(Path.Combine(this.temp.Path, "v1.75.0", "buf.exe"), exe);
        Assert.Contains(log, line => line.Contains("Signature of sha256.txt verified", StringComparison.Ordinal));
        var binary = await BufCli.ResolveAsync(exe, baseDirectory: null, TestContext.Current.CancellationToken);
        Assert.Equal(new Version(1, 75, 0), binary?.Version);
    }
}
