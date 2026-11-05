using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Unit;

public sealed class WorkspaceForPathTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    [Fact]
    public void A_file_belongs_to_the_nearest_workspace_above_it()
    {
        this.temp.File(@"repo\.git\HEAD");
        this.temp.File(@"repo\api\buf.yaml");
        var proto = this.temp.File(@"repo\api\proto\a\v1\a.proto");

        Assert.Equal(Path.Combine(this.temp.Path, "repo", "api"), WorkspaceRoots.ForPath(proto, Path.Combine(this.temp.Path, "repo")));
    }

    [Fact]
    public void The_outermost_config_wins_like_the_solution_wide_search()
    {
        this.temp.File(@"repo\.git\HEAD");
        this.temp.File(@"repo\buf.work.yaml");
        this.temp.File(@"repo\mod\buf.yaml");
        var proto = this.temp.File(@"repo\mod\x.proto");

        Assert.Equal(Path.Combine(this.temp.Path, "repo"), WorkspaceRoots.ForPath(proto, null));
    }

    [Fact]
    public void The_search_stops_at_the_git_repository_root()
    {
        this.temp.File("buf.yaml");                       // above the repository: must be ignored
        this.temp.File(@"repo\.git\HEAD");
        var proto = this.temp.File(@"repo\protos\x.proto");

        Assert.Equal(Path.Combine(this.temp.Path, "repo"), WorkspaceRoots.ForPath(proto, Path.Combine(this.temp.Path, "repo")));
    }

    [Fact]
    public void A_git_file_also_marks_the_repository_root()
    {
        this.temp.File("buf.yaml");
        this.temp.File(@"worktree\.git", "gitdir: elsewhere");
        var proto = this.temp.File(@"worktree\x.proto");

        Assert.Equal(Path.Combine(this.temp.Path, "worktree"), WorkspaceRoots.ForPath(proto, null));
    }

    [Fact]
    public void Without_config_a_path_outside_the_solution_uses_its_own_folder()
    {
        this.temp.File(@"repo\.git\HEAD");
        var proto = this.temp.File(@"repo\loose\x.proto");

        Assert.Equal(Path.Combine(this.temp.Path, "repo", "loose"), WorkspaceRoots.ForPath(proto, Path.Combine(this.temp.Path, "elsewhere")));
    }

    [Fact]
    public void A_folder_resolves_from_itself()
    {
        this.temp.File(@"repo\.git\HEAD");
        this.temp.File(@"repo\buf.yaml");
        var folder = this.temp.Folder(@"repo\proto\acme");

        Assert.Equal(Path.Combine(this.temp.Path, "repo"), WorkspaceRoots.ForPath(folder, null));
    }

    [Theory]
    [InlineData(@"C:\ws", @"C:\ws\proto\a.proto", "proto/a.proto")]
    [InlineData(@"C:\ws", @"C:\ws\proto\", "proto")]
    [InlineData(@"C:\ws", @"C:\ws", ".")]
    public void Relative_arguments_use_forward_slashes(string root, string path, string expected)
    {
        Assert.Equal(expected, WorkspaceRoots.RelativeArgument(root, path));
    }

    [Theory]
    [InlineData(@"C:\ws\a.proto", @"C:\ws", true)]
    [InlineData(@"C:\WS\sub", @"C:\ws\", true)]
    [InlineData(@"C:\ws", @"C:\ws", true)]
    [InlineData(@"C:\wsx\a.proto", @"C:\ws", false)]
    public void IsUnder_compares_whole_path_segments(string path, string directory, bool expected)
    {
        Assert.Equal(expected, WorkspaceRoots.IsUnder(path, directory));
    }
}
