using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Integration;

/// <summary>The Solution Explorer commands: resolve the workspace for a path, then run buf on just that path.</summary>
[Trait("Category", "Integration")]
public sealed class PathCommandsIntegrationTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    private static string Weather => Path.Combine(RealBuf.WeatherSample, "proto", "acme", "weather", "v1", "weather.proto");

    private static string Location => Path.Combine(RealBuf.WeatherSample, "proto", "acme", "common", "v1", "location.proto");

    [Fact]
    public void Sample_files_resolve_to_the_sample_workspace()
    {
        Assert.Equal(RealBuf.WeatherSample, WorkspaceRoots.ForPath(Weather, solutionDirectory: null));
        Assert.Equal("proto/acme/weather/v1/weather.proto", WorkspaceRoots.RelativeArgument(RealBuf.WeatherSample, Weather));
    }

    [Fact]
    public async Task Linting_one_file_reports_only_that_file()
    {
        var findings = await this.LintAsync(Weather);

        var finding = Assert.Single(findings);
        Assert.Equal("FIELD_LOWER_SNAKE_CASE", finding.Type);
        Assert.Equal(Weather, finding.Path);
    }

    [Fact]
    public async Task Linting_a_clean_file_or_folder_reports_nothing()
    {
        Assert.Empty(await this.LintAsync(Location));
        Assert.Empty(await this.LintAsync(Path.GetDirectoryName(Location)!));
    }

    [Fact]
    public async Task Linting_the_workspace_folder_lints_everything()
    {
        var finding = Assert.Single(await this.LintAsync(RealBuf.WeatherSample));
        Assert.Equal(Weather, finding.Path);
    }

    [Fact]
    public async Task Linting_a_subfolder_of_a_module_uses_a_path_filter()
    {
        var finding = Assert.Single(await this.LintAsync(Path.Combine(RealBuf.WeatherSample, "proto", "acme")));
        Assert.Equal(Weather, finding.Path);
    }

    [Fact]
    public async Task Linting_a_module_root_passes_it_as_the_input()
    {
        var finding = Assert.Single(await this.LintAsync(Path.Combine(RealBuf.WeatherSample, "proto")));
        Assert.Equal(Weather, finding.Path);
    }

    [Fact]
    public async Task Format_rewrites_only_the_chosen_file()
    {
        var workspace = this.temp.Folder("ws");
        File.WriteAllText(Path.Combine(workspace, "buf.yaml"), "version: v2\n");
        var messy = Path.Combine(workspace, "messy.proto");
        var other = Path.Combine(workspace, "other.proto");
        const string Unformatted = "syntax=\"proto3\";package demo;message   A{string  name=1;}\n";
        File.WriteAllText(messy, Unformatted);
        File.WriteAllText(other, Unformatted);

        var buf = await RealBuf.GetAsync();
        var root = WorkspaceRoots.ForPath(messy, solutionDirectory: null);
        var result = await BufCli.RunAsync(buf.Path, BufPathCommands.Format(root, WorkspaceRoots.RelativeArgument(root, messy)), root, TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.StandardError);
        var formatted = File.ReadAllText(messy);
        Assert.Contains("message A {", formatted, StringComparison.Ordinal);
        Assert.Contains("  string name = 1;", formatted, StringComparison.Ordinal);
        Assert.Equal(Unformatted, File.ReadAllText(other));
    }

    private async Task<IReadOnlyList<BufAnnotation>> LintAsync(string path)
    {
        var buf = await RealBuf.GetAsync();
        var root = WorkspaceRoots.ForPath(path, solutionDirectory: null);
        var result = await BufCli.RunAsync(buf.Path, BufPathCommands.Lint(root, WorkspaceRoots.RelativeArgument(root, path)), root, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode is 0 or BufCheck.FindingsExitCode, $"buf lint failed ({result.ExitCode}): {result.StandardError}");
        return BufAnnotation.ParseOutput(result, root);
    }
}
