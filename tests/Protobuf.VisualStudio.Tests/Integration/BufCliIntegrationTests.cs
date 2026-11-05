using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Integration;

/// <summary>Runs the real buf.exe against samples\weather, the way the Tools &gt; Buf commands do.</summary>
[Trait("Category", "Integration")]
public sealed class BufCliIntegrationTests
{
    [Fact]
    public async Task Installed_buf_is_new_enough_for_the_language_server()
    {
        var buf = await RealBuf.GetAsync();

        Assert.True(buf.SupportsLanguageServer, $"buf {buf.Version} at {buf.Path} has no language server.");
        Assert.True(buf.Version >= BufBinary.MinimumLspVersion, $"buf {buf.Version} predates 'buf lsp serve'.");
    }

    [Fact]
    public void The_sample_is_one_workspace()
    {
        Assert.Equal([RealBuf.WeatherSample], WorkspaceRoots.Find(RealBuf.WeatherSample));
    }

    [Fact]
    public async Task Build_compiles_the_sample()
    {
        var result = await RunAsync("build", "--error-format", "msvs");

        Assert.True(result.ExitCode == 0, $"buf build failed ({result.ExitCode}):\n{result.StandardOutput}\n{result.StandardError}");
    }

    [Fact]
    public async Task Lint_reports_the_deliberate_violation_in_msvs_format()
    {
        var result = await RunAsync("lint", "--error-format", "msvs");
        var output = result.StandardOutput + result.StandardError;

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("FIELD_LOWER_SNAKE_CASE", output, StringComparison.Ordinal);

        // msvs format, "path(line,column) : error CODE : message", is what makes the line clickable in the Output pane.
        Assert.Matches(@"weather\.proto\(\d+,\d+\) ?: ?error", output);
    }

    [Fact]
    public async Task Lint_rules_can_be_listed()
    {
        var result = await RunAsync("config", "ls-lint-rules");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("FIELD_LOWER_SNAKE_CASE", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_a_run_throws_and_leaves_no_buf_process()
    {
        var buf = await RealBuf.GetAsync();
        var before = BufProcessIds(buf.Path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BufCli.RunAsync(buf.Path, ["build"], RealBuf.WeatherSample, new CancellationToken(canceled: true)));

        Assert.Empty(BufProcessIds(buf.Path).Except(before));
    }

    private static int[] BufProcessIds(string path) =>
        System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)).Select(p => p.Id).ToArray();

    private static async Task<BufResult> RunAsync(params string[] args)
    {
        var buf = await RealBuf.GetAsync();
        return await BufCli.RunAsync(buf.Path, args, RealBuf.WeatherSample, TestContext.Current.CancellationToken);
    }
}
