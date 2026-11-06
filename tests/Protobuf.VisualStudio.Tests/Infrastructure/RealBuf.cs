using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Tests.Infrastructure;

/// <summary>
/// The real buf.exe used by integration tests. <c>BUF_PATH</c> wins; otherwise the extension's own
/// <see cref="BufInstaller"/> installs the latest release into a cache under %TEMP% (downloaded once, then reused),
/// so these tests also exercise the real download and checksum path against GitHub.
/// </summary>
internal static class RealBuf
{
    public static readonly string CacheRoot = Path.Combine(Path.GetTempPath(), "protobuf-vs-tests", "buf-cache");

    private static readonly Lazy<Task<BufBinary>> Binary = new(ResolveAsync);

    public static Task<BufBinary> GetAsync() => Binary.Value;

    /// <summary>The repository's samples\weather folder.</summary>
    public static string WeatherSample { get; } = Path.Combine(RepoRoot(), "samples", "weather");

    /// <summary>
    /// The sample's source files as paths relative to it: everything but generated code, build output and the
    /// .vs folder (which Visual Studio keeps locked while the sample solution is open).
    /// </summary>
    public static IEnumerable<string> WeatherSampleFiles() =>
        Directory.EnumerateFiles(WeatherSample, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(WeatherSample, f))
            .Where(r => r.Split(Path.DirectorySeparatorChar)[0].ToLowerInvariant() is not ("gen" or "bin" or "obj" or ".vs"));

    private static async Task<BufBinary> ResolveAsync()
    {
        var configured = Environment.GetEnvironmentVariable("BUF_PATH");
        string path;
        if (string.IsNullOrWhiteSpace(configured))
        {
            path = await BufInstaller.InstallAsync(CacheRoot, _ => { }, progress: null, CancellationToken.None).ConfigureAwait(false);
        }
        else if (Path.IsPathRooted(configured))
        {
            path = configured;
        }
        else
        {
            // bufbuild/buf-action reports a bare "buf.exe" that is on PATH; resolve it the way the extension does.
            path = BufCli.Locate(null, baseDirectory: null, Environment.GetEnvironmentVariable("PATH"), installRoot: CacheRoot)
                ?? throw new InvalidOperationException($"BUF_PATH is '{configured}', but no buf.exe is on PATH.");
        }

        return await BufCli.ResolveAsync(path, baseDirectory: null, CancellationToken.None).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"'{path}' is not a working buf.exe.");
    }

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProtobufForVisualStudio.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root (ProtobufForVisualStudio.slnx).");
    }
}
