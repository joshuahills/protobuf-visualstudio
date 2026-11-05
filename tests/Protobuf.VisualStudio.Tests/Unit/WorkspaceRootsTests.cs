using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Unit;

public sealed class WorkspaceRootsTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    [Fact]
    public void Without_any_buf_config_the_solution_folder_is_the_root()
    {
        this.temp.File(@"protos\a.proto");
        Assert.Equal([this.temp.Path], WorkspaceRoots.Find(this.temp.Path));
    }

    [Fact]
    public void A_root_buf_yaml_covers_nested_modules()
    {
        this.temp.File("buf.yaml", "version: v2");
        this.temp.File(@"nested\buf.yaml", "version: v1");
        Assert.Equal([this.temp.Path], WorkspaceRoots.Find(this.temp.Path));
    }

    [Fact]
    public void Sibling_workspaces_are_all_returned()
    {
        var api = this.temp.File(@"services\api\buf.yaml");
        var events = this.temp.File(@"services\events\buf.work.yaml");

        var roots = WorkspaceRoots.Find(this.temp.Path).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.Equal([Path.GetDirectoryName(api)!, Path.GetDirectoryName(events)!], roots);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".vs")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData("worktrees")]
    public void Build_output_and_tool_folders_are_skipped(string folder)
    {
        this.temp.File($@"{folder}\copy\buf.yaml");
        Assert.Equal([this.temp.Path], WorkspaceRoots.Find(this.temp.Path));
    }

    [Fact]
    public void Search_stops_at_the_depth_limit()
    {
        this.temp.File(@"1\2\3\buf.yaml");
        Assert.Equal([this.temp.Path], WorkspaceRoots.Find(this.temp.Path, maxDepth: 2));
        Assert.Equal([Path.Combine(this.temp.Path, "1", "2", "3")], WorkspaceRoots.Find(this.temp.Path, maxDepth: 3));
    }
}
