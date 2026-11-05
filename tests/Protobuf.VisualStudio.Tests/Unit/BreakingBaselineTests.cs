using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Tests.Unit;

public class BreakingBaselineTests
{
    [Fact]
    public void Workspace_at_the_repository_root_has_no_subdir()
    {
        Assert.Equal(".git#branch=main", BreakingBaseline.FormatGitInput(@"C:\repo", @"C:\repo", "branch", "main"));
    }

    [Fact]
    public void Nested_workspace_points_back_to_the_git_dir_with_a_forward_slash_subdir()
    {
        Assert.Equal(
            "../../.git#branch=main,subdir=samples/weather",
            BreakingBaseline.FormatGitInput(@"C:\repo\samples\weather", @"C:\repo", "branch", "main"));
    }

    [Fact]
    public void Git_style_forward_slash_top_level_is_accepted()
    {
        // 'git rev-parse --show-toplevel' prints C:/repo on Windows.
        Assert.Equal("../.git#ref=refs/remotes/origin/main,subdir=proto", BreakingBaseline.FormatGitInput(@"C:\repo\proto", "C:/repo", "ref", "refs/remotes/origin/main"));
    }

    [Fact]
    public async Task A_configured_baseline_is_used_verbatim_without_calling_git()
    {
        var git = new FakeGit();
        var result = await BreakingBaseline.ResolveAsync(@"C:\repo", "  buf.build/acme/weather ", git.RunAsync, TestContext.Current.CancellationToken);

        Assert.Equal("buf.build/acme/weather", result.Against);
        Assert.Empty(git.Calls);
    }

    [Fact]
    public async Task Outside_a_repository_explains_how_to_configure_a_baseline()
    {
        var git = new FakeGit();
        var result = await BreakingBaseline.ResolveAsync(@"C:\nogit", null, git.RunAsync, TestContext.Current.CancellationToken);

        Assert.Null(result.Against);
        Assert.Contains("not in a git repository", result.Error, StringComparison.Ordinal);
        Assert.Contains("Breaking change baseline", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prefers_the_branch_origin_HEAD_points_to()
    {
        var git = new FakeGit { TopLevel = "C:/repo", OriginHead = "origin/develop", Refs = { "refs/heads/develop", "refs/heads/main" } };
        var result = await BreakingBaseline.ResolveAsync(@"C:\repo", null, git.RunAsync, TestContext.Current.CancellationToken);

        Assert.Equal(".git#branch=develop", result.Against);
    }

    [Theory]
    [InlineData("refs/heads/main", ".git#branch=main")]
    [InlineData("refs/heads/master", ".git#branch=master")]
    [InlineData("refs/remotes/origin/main", ".git#ref=refs/remotes/origin/main")]
    public async Task Falls_back_to_main_then_master_then_the_remote_tracking_ref(string existingRef, string expected)
    {
        var git = new FakeGit { TopLevel = "C:/repo", Refs = { existingRef } };
        var result = await BreakingBaseline.ResolveAsync(@"C:\repo", null, git.RunAsync, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Against);
    }

    [Fact]
    public async Task No_usable_branch_is_reported()
    {
        var git = new FakeGit { TopLevel = "C:/repo", Refs = { "refs/heads/feature" } };
        var result = await BreakingBaseline.ResolveAsync(@"C:\repo", null, git.RunAsync, TestContext.Current.CancellationToken);

        Assert.Null(result.Against);
        Assert.Contains("main or master", result.Error, StringComparison.Ordinal);
    }

    /// <summary>Answers the handful of git commands the resolver uses.</summary>
    private sealed class FakeGit
    {
        public string? TopLevel { get; init; }

        public string? OriginHead { get; init; }

        public HashSet<string> Refs { get; } = [];

        public List<string> Calls { get; } = [];

        public Task<(int, string)> RunAsync(string directory, IReadOnlyList<string> args, CancellationToken cancellationToken)
        {
            this.Calls.Add(string.Join(' ', args));
            (int, string) result = args switch
            {
                ["rev-parse", "--show-toplevel"] => this.TopLevel is null ? (128, string.Empty) : (0, this.TopLevel),
                ["symbolic-ref", .., "refs/remotes/origin/HEAD"] => this.OriginHead is null ? (1, string.Empty) : (0, this.OriginHead),
                ["rev-parse", "--verify", "--quiet", var reference] => this.Refs.Contains(reference) ? (0, "abc123") : (1, string.Empty),
                _ => throw new InvalidOperationException($"Unexpected git call: {string.Join(' ', args)}"),
            };
            return Task.FromResult(result);
        }
    }
}
