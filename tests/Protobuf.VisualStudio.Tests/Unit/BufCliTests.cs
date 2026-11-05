using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Unit;

public sealed class BufCliTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => this.temp.Dispose();

    [Theory]
    [InlineData("1.75.0", "1.75.0")]
    [InlineData("1.75.0\r\n", "1.75.0")]
    [InlineData("buf version v1.43.2", "1.43.2")]
    [InlineData("1.50.0-dev", "1.50.0")]
    public void ParseVersion_reads_the_first_semver(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), BufCli.ParseVersion(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("command not found")]
    [InlineData("1.75")]
    public void ParseVersion_returns_null_without_a_version(string? text)
    {
        Assert.Null(BufCli.ParseVersion(text));
    }

    [Fact]
    public void Locate_uses_a_configured_file()
    {
        var exe = this.temp.File(@"tools\buf.exe");
        Assert.Equal(exe, BufCli.Locate(exe, baseDirectory: null, pathVariable: null, this.InstallRoot));
    }

    [Fact]
    public void Locate_accepts_a_configured_folder()
    {
        var exe = this.temp.File(@"tools\buf.exe");
        Assert.Equal(exe, BufCli.Locate(Path.GetDirectoryName(exe), baseDirectory: null, pathVariable: null, this.InstallRoot));
    }

    [Fact]
    public void Locate_resolves_relative_paths_against_the_solution_folder()
    {
        var exe = this.temp.File(@"repo\tools\buf.exe");
        var located = BufCli.Locate(@"tools\buf.exe", Path.Combine(this.temp.Path, "repo"), pathVariable: null, this.InstallRoot);
        Assert.Equal(exe, located);
    }

    [Fact]
    public void Locate_expands_environment_variables()
    {
        var exe = this.temp.File(@"env\buf.exe");
        var name = "PROTOBUF_VS_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, Path.GetDirectoryName(exe));
        try
        {
            Assert.Equal(exe, BufCli.Locate($"%{name}%\\buf.exe", baseDirectory: null, pathVariable: null, this.InstallRoot));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Locate_does_not_fall_back_when_the_configured_path_is_wrong()
    {
        var onPath = this.temp.File(@"path\buf.exe");
        var located = BufCli.Locate(@"C:\does\not\exist\buf.exe", baseDirectory: null, Path.GetDirectoryName(onPath), this.InstallRoot);
        Assert.Null(located);
    }

    [Fact]
    public void Locate_searches_PATH_in_order_and_tolerates_quotes_and_junk()
    {
        var first = this.temp.File(@"a\buf.exe");
        this.temp.File(@"b\buf.exe");
        var path = string.Join(';', "", "  ", "C:\\nope<>|", $"\"{Path.GetDirectoryName(first)}\"", Path.Combine(this.temp.Path, "b"));
        Assert.Equal(first, BufCli.Locate(null, baseDirectory: null, path, this.InstallRoot));
    }

    [Fact]
    public void Locate_falls_back_to_the_installed_copy()
    {
        var installed = this.temp.File(@"install\v1.75.0\buf.exe");
        Assert.Equal(installed, BufCli.Locate(null, baseDirectory: null, pathVariable: string.Empty, this.InstallRoot));
    }

    [Fact]
    public void Locate_returns_null_when_buf_is_nowhere()
    {
        Assert.Null(BufCli.Locate(" ", baseDirectory: null, pathVariable: this.temp.Path, this.InstallRoot));
    }

    [Fact]
    public void A_pinned_version_uses_that_installed_copy_instead_of_PATH()
    {
        var onPath = this.temp.File(@"path\buf.exe");
        this.temp.File(@"install\v1.80.0\buf.exe");
        var pinned = this.temp.File(@"install\v1.70.0\buf.exe");

        Assert.Equal(pinned, BufCli.Locate(null, baseDirectory: null, Path.GetDirectoryName(onPath), this.InstallRoot, pinnedVersion: " v1.70.0 "));
    }

    [Fact]
    public void A_pinned_version_that_is_not_installed_finds_nothing_so_it_gets_downloaded()
    {
        var onPath = this.temp.File(@"path\buf.exe");
        this.temp.File(@"install\v1.80.0\buf.exe");

        Assert.Null(BufCli.Locate(null, baseDirectory: null, Path.GetDirectoryName(onPath), this.InstallRoot, pinnedVersion: "1.70.0"));
    }

    [Fact]
    public void A_configured_path_wins_over_a_pinned_version()
    {
        var configured = this.temp.File(@"tools\buf.exe");
        this.temp.File(@"install\v1.70.0\buf.exe");

        Assert.Equal(configured, BufCli.Locate(configured, baseDirectory: null, pathVariable: null, this.InstallRoot, pinnedVersion: "1.70.0"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("1.75.0", "1.75.0")]
    [InlineData(" v1.75.0 ", "1.75.0")]
    [InlineData("V2.0.0", "2.0.0")]
    public void NormaliseVersion_strips_whitespace_and_a_leading_v(string? input, string? expected)
    {
        Assert.Equal(expected, BufCli.NormaliseVersion(input));
    }

    [Fact]
    public void FindInstalled_picks_the_highest_version_numerically()
    {
        this.temp.File(@"install\v1.9.0\buf.exe");
        var newest = this.temp.File(@"install\v1.10.0\buf.exe");
        this.temp.Folder(@"install\v2.0.0");               // no buf.exe: ignored
        this.temp.File(@"install\vnext\buf.exe");          // not a version: ignored

        Assert.Equal(newest, BufCli.FindInstalled(this.InstallRoot));
    }

    [Fact]
    public void FindInstalled_returns_null_for_a_missing_root()
    {
        Assert.Null(BufCli.FindInstalled(Path.Combine(this.temp.Path, "missing")));
    }

    [Fact]
    public void FormatCommandLine_quotes_arguments_with_spaces()
    {
        Assert.Equal("buf lint --path \"a b.proto\"", BufCli.FormatCommandLine(["lint", "--path", "a b.proto"]));
    }

    [Fact]
    public void CreateStartInfo_passes_arguments_verbatim_and_redirects_stdio()
    {
        var info = BufCli.CreateStartInfo(@"C:\buf.exe", ["lsp", "serve", "with space"], this.temp.Path);

        Assert.Equal(["lsp", "serve", "with space"], info.ArgumentList);
        Assert.Equal(this.temp.Path, info.WorkingDirectory);
        Assert.True(info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
    }

    [Fact]
    public void CreateStartInfo_ignores_a_missing_working_directory()
    {
        var info = BufCli.CreateStartInfo(@"C:\buf.exe", [], Path.Combine(this.temp.Path, "missing"));
        Assert.Equal(string.Empty, info.WorkingDirectory);
    }

    private string InstallRoot => Path.Combine(this.temp.Path, "install");
}
