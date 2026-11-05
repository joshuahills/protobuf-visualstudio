using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Integration;

/// <summary>Check Breaking Changes end to end: real git repositories, the resolver, and the real buf breaking.</summary>
[Trait("Category", "Integration")]
public sealed class BreakingIntegrationTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    [Fact]
    public async Task Deleting_a_field_is_reported_against_the_default_branch()
    {
        var repo = await this.CreateRepoAsync(this.temp.Folder("repo"), workspaceSubdir: null);
        DeleteField(repo);

        var findings = await RunBreakingAsync(repo);

        var finding = Assert.Single(findings);
        Assert.Equal("FIELD_NO_DELETE", finding.Type);
        Assert.Contains("temperature_celsius", finding.Message, StringComparison.Ordinal);
        Assert.Equal(BufSeverity.Error, BufDiagnosticMapper.ToDiagnostic(finding, BufCheckKind.Breaking).Severity);
    }

    [Fact]
    public async Task Unchanged_workspace_has_no_breaking_changes()
    {
        var repo = await this.CreateRepoAsync(this.temp.Folder("clean"), workspaceSubdir: null);

        Assert.Empty(await RunBreakingAsync(repo));
    }

    [Fact]
    public async Task Workspace_below_the_repository_root_uses_subdir()
    {
        var root = this.temp.Folder("mono");
        var workspace = await this.CreateRepoAsync(root, workspaceSubdir: Path.Combine("apis", "weather"));
        DeleteField(workspace);

        var baseline = await BreakingBaseline.ResolveAsync(workspace, null, BreakingBaseline.RunGitAsync, TestContext.Current.CancellationToken);
        Assert.Equal("../../.git#branch=main,subdir=apis/weather", baseline.Against);

        var finding = Assert.Single(await RunBreakingAsync(workspace));
        Assert.Equal("FIELD_NO_DELETE", finding.Type);
    }

    [Fact]
    public async Task A_clone_on_another_branch_compares_with_origin_main()
    {
        var origin = await this.CreateRepoAsync(this.temp.Folder("origin"), workspaceSubdir: null);
        var clone = Path.Combine(this.temp.Path, "clone");
        await GitAsync(this.temp.Path, "clone", "--quiet", origin, clone);
        await GitAsync(clone, "checkout", "--quiet", "-b", "feature");
        await GitAsync(clone, "branch", "--quiet", "-D", "main");
        DeleteField(clone);

        var baseline = await BreakingBaseline.ResolveAsync(clone, null, BreakingBaseline.RunGitAsync, TestContext.Current.CancellationToken);
        Assert.Equal(".git#ref=refs/remotes/origin/main", baseline.Against);

        var finding = Assert.Single(await RunBreakingAsync(clone));
        Assert.Equal("FIELD_NO_DELETE", finding.Type);
    }

    [Fact]
    public async Task Outside_git_the_resolver_explains_itself()
    {
        var plain = this.temp.Folder("plain");
        var baseline = await BreakingBaseline.ResolveAsync(plain, null, BreakingBaseline.RunGitAsync, TestContext.Current.CancellationToken);

        Assert.Null(baseline.Against);
        Assert.Contains("not in a git repository", baseline.Error, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<BufAnnotation>> RunBreakingAsync(string workspace)
    {
        var baseline = await BreakingBaseline.ResolveAsync(workspace, null, BreakingBaseline.RunGitAsync, TestContext.Current.CancellationToken);
        Assert.True(baseline.Against is not null, baseline.Error);

        var buf = await RealBuf.GetAsync();
        var result = await BufCli.RunAsync(buf.Path, ["breaking", "--against", baseline.Against!, "--error-format", "json"], workspace, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode is 0 or BufCheck.FindingsExitCode, $"buf breaking failed ({result.ExitCode}): {result.StandardError}");
        return BufAnnotation.ParseOutput(result, workspace);
    }

    private static void DeleteField(string workspace)
    {
        var weather = Path.Combine(workspace, "proto", "acme", "weather", "v1", "weather.proto");
        var lines = File.ReadAllLines(weather).Where(l => !l.Contains("temperature_celsius", StringComparison.Ordinal));
        File.WriteAllLines(weather, lines);
    }

    /// <summary>Creates a git repo on branch main with the weather sample committed; returns the workspace folder.</summary>
    private async Task<string> CreateRepoAsync(string repoRoot, string? workspaceSubdir)
    {
        var workspace = workspaceSubdir is null ? repoRoot : Path.Combine(repoRoot, workspaceSubdir);
        foreach (var file in Directory.EnumerateFiles(RealBuf.WeatherSample, "*.*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RealBuf.WeatherSample, file);
            if (!relative.StartsWith("gen", StringComparison.OrdinalIgnoreCase))
            {
                var destination = Path.Combine(workspace, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }

        await GitAsync(repoRoot, "init", "--quiet", "-b", "main");
        await GitAsync(repoRoot, "add", "-A");
        await GitAsync(repoRoot, "-c", "user.email=tests@example.invalid", "-c", "user.name=Tests", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "base");
        return workspace;
    }

    private static async Task GitAsync(string directory, params string[] args)
    {
        var result = await BufCli.RunAsync("git", args, directory, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)} failed: {result.StandardError}");
    }
}
