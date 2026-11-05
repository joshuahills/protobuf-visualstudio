using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Unit;

public sealed class BufPathCommandsTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    [Fact]
    public void Reads_v2_module_paths()
    {
        const string Yaml = """
            version: v2
            modules:
              - path: proto
                excludes:
                  - proto/vendor
              - path: "./third_party/protos/"   # vendored
            lint:
              use:
                - STANDARD
            """;

        Assert.Equal(["proto", "third_party/protos"], BufPathCommands.ModuleRoots(Yaml, null));
    }

    [Fact]
    public void Reads_v1_workspace_directories()
    {
        const string Work = """
            version: v1
            directories:
            - api
            - ./vendor\protos
            """;

        Assert.Equal(["api", "vendor/protos"], BufPathCommands.ModuleRoots(null, Work));
    }

    [Fact]
    public void A_root_module_is_not_a_subfolder()
    {
        Assert.Empty(BufPathCommands.ModuleRoots("version: v2\nmodules:\n  - path: .\n", null));
    }

    [Fact]
    public void The_workspace_itself_takes_no_path()
    {
        Assert.Equal(["lint", "--error-format", "json"], BufPathCommands.Lint(this.temp.Path, "."));
    }

    [Fact]
    public void A_module_root_is_the_input()
    {
        this.temp.File("buf.yaml", "version: v2\nmodules:\n  - path: proto\n");

        Assert.Equal(["lint", "proto", "--error-format", "json"], BufPathCommands.Lint(this.temp.Path, "proto"));
        Assert.Equal(["lint", "proto/", "--error-format", "json"], BufPathCommands.Lint(this.temp.Path, "proto/"));
    }

    [Fact]
    public void Anything_else_is_a_path_filter()
    {
        this.temp.File("buf.yaml", "version: v2\nmodules:\n  - path: proto\n");

        Assert.Equal(["lint", "--path", "proto/acme/a.proto", "--error-format", "json"], BufPathCommands.Lint(this.temp.Path, "proto/acme/a.proto"));
        Assert.Equal(["lint", "--path", "protos", "--error-format", "json"], BufPathCommands.Lint(this.temp.Path, "protos"));
    }

    [Fact]
    public void Format_rewrites_the_file_given_as_input()
    {
        Assert.Equal(["format", "-w", "proto/a.proto"], BufPathCommands.Format(this.temp.Path, "proto/a.proto"));
    }
}
