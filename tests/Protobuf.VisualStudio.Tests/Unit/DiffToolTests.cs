using System.Diagnostics;
using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Unit;

public sealed class DiffToolTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    [Fact]
    public void Nothing_is_added_when_PATH_already_has_a_diff()
    {
        var tools = this.Dir("tools", "diff.exe");
        var fallback = this.Dir("fallback", "diff.exe");

        Assert.Null(DiffTool.FindDirectory($"{this.Dir("empty")};{tools}", [fallback]));
    }

    [Theory]
    [InlineData(@"Git\cmd")]
    [InlineData(@"Git\mingw64\bin")]
    public void Uses_usr_bin_of_the_Git_on_PATH(string gitDirectory)
    {
        var git = this.Dir(gitDirectory, "git.exe");
        var usrBin = this.Dir(@"Git\usr\bin", "diff.exe");
        var fallback = this.Dir("fallback", "diff.exe");

        Assert.Equal(usrBin, DiffTool.FindDirectory(git, [fallback]));
    }

    [Fact]
    public void Falls_back_to_the_first_candidate_with_a_diff()
    {
        var missing = Path.Combine(this.temp.Path, "missing");
        var noDiff = this.Dir("nodiff");
        var bundled = this.Dir(@"VS\Git\usr\bin", "diff.exe");

        Assert.Equal(bundled, DiffTool.FindDirectory(this.Dir("empty"), [missing, noDiff, bundled]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(";;\"C:\\bad|path\";")]
    public void Copes_with_empty_or_odd_PATH_values(string? pathVariable)
    {
        var bundled = this.Dir("bundled", "diff.exe");

        Assert.Equal(bundled, DiffTool.FindDirectory(pathVariable, [bundled]));
    }

    [Fact]
    public void Returns_null_when_there_is_no_diff_anywhere()
    {
        Assert.Null(DiffTool.FindDirectory(this.Dir("empty"), [this.Dir("nodiff")]));
    }

    [Fact]
    public void Appends_the_directory_to_the_child_PATH()
    {
        var info = new ProcessStartInfo("buf.exe");
        info.Environment["PATH"] = @"C:\first;";

        BufCli.AddDiffToPath(info, @"C:\Git\usr\bin");

        Assert.Equal(@"C:\first;C:\Git\usr\bin", info.Environment["PATH"]);
    }

    [Fact]
    public void Leaves_PATH_alone_when_nothing_needs_adding()
    {
        var info = new ProcessStartInfo("buf.exe");
        info.Environment["PATH"] = @"C:\first";

        BufCli.AddDiffToPath(info, null);

        Assert.Equal(@"C:\first", info.Environment["PATH"]);
    }

    private string Dir(string relative, string? file = null)
    {
        var directory = Path.Combine(this.temp.Path, relative);
        Directory.CreateDirectory(directory);
        if (file is not null)
        {
            File.WriteAllText(Path.Combine(directory, file), string.Empty);
        }

        return directory;
    }
}
