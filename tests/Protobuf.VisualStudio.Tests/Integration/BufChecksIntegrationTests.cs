using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Integration;

/// <summary>
/// Runs the Tools &gt; Buf checks the way <see cref="BufService"/> does (JSON output, parsed, mapped to diagnostics)
/// against the real buf.exe.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BufChecksIntegrationTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    [Fact]
    public async Task Lint_finding_maps_to_an_Error_List_warning_on_the_real_file()
    {
        var (exitCode, annotations) = await RunCheckAsync(RealBuf.WeatherSample, "lint", "--error-format", "json");

        Assert.Equal(BufCheck.FindingsExitCode, exitCode);
        var lint = Assert.Single(annotations);
        Assert.Equal(Path.Combine(RealBuf.WeatherSample, "proto", "acme", "weather", "v1", "weather.proto"), lint.Path);
        Assert.True(File.Exists(lint.Path));

        var diagnostic = BufDiagnosticMapper.ToDiagnostic(lint, BufCheckKind.Lint);
        Assert.Equal("FIELD_LOWER_SNAKE_CASE", diagnostic.Code);
        Assert.Equal(LineOf(lint.Path, "windSpeedKph"), diagnostic.StartLine);
    }

    [Fact]
    public async Task Clean_build_reports_nothing()
    {
        var (exitCode, annotations) = await RunCheckAsync(RealBuf.WeatherSample, "build", "--error-format", "json");

        Assert.Equal(0, exitCode);
        Assert.Empty(annotations);
    }

    [Fact]
    public async Task Compile_error_maps_to_an_Error_List_error()
    {
        var workspace = this.CopySample();
        var weather = Path.Combine(workspace, "proto", "acme", "weather", "v1", "weather.proto");
        File.WriteAllText(weather, File.ReadAllText(weather).Replace("Observation observation = 1;", "Observashun observation = 1;", StringComparison.Ordinal));

        var (exitCode, annotations) = await RunCheckAsync(workspace, "build", "--error-format", "json");

        Assert.Equal(BufCheck.FindingsExitCode, exitCode);
        var error = Assert.Single(annotations);
        Assert.Equal(BufAnnotation.CompileType, error.Type);
        Assert.Contains("Observashun", error.Message, StringComparison.Ordinal);
        Assert.Equal(BufSeverity.Error, BufDiagnosticMapper.ToDiagnostic(error, BufCheckKind.Build).Severity);
        Assert.Equal(LineOf(weather, "Observashun"), error.StartLine - 1);
    }

    internal static async Task<(int ExitCode, IReadOnlyList<BufAnnotation> Annotations)> RunCheckAsync(string workspace, params string[] args)
    {
        var buf = await RealBuf.GetAsync();
        var result = await BufCli.RunAsync(buf.Path, args, workspace, TestContext.Current.CancellationToken);
        return (result.ExitCode, BufAnnotation.ParseOutput(result, workspace));
    }

    /// <summary>Copies samples\weather (without gen\) into a scratch folder and returns its path.</summary>
    internal string CopySample()
    {
        var target = this.temp.Folder("weather");
        foreach (var file in Directory.EnumerateFiles(RealBuf.WeatherSample, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RealBuf.WeatherSample, file);
            if (relative.StartsWith("gen" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return target;
    }

    internal static int LineOf(string path, string needle) =>
        Array.FindIndex(File.ReadAllLines(path), line => line.Contains(needle, StringComparison.Ordinal));
}
