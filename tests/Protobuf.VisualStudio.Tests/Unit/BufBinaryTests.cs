using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Tests.Unit;

public class BufBinaryTests
{
    [Theory]
    [InlineData("1.42.9", false)]
    [InlineData("1.43.0", true)]
    [InlineData("1.75.0", true)]
    public void SupportsLanguageServer_starts_at_1_43(string version, bool expected)
    {
        Assert.Equal(expected, new BufBinary("buf.exe", Version.Parse(version)).SupportsLanguageServer);
    }

    [Fact]
    public void Uses_lsp_serve_from_1_59()
    {
        var args = new BufBinary("buf.exe", new Version(1, 59, 0)).LanguageServerArguments(debug: false);
        Assert.Equal(["--log-format", "text", "lsp", "serve"], args);
    }

    [Fact]
    public void Uses_beta_lsp_before_1_59()
    {
        var args = new BufBinary("buf.exe", new Version(1, 58, 9)).LanguageServerArguments(debug: false);
        Assert.Equal(["--log-format", "text", "beta", "lsp"], args);
    }

    [Fact]
    public void Debug_adds_the_flag_before_the_subcommand()
    {
        var args = new BufBinary("buf.exe", new Version(1, 75, 0)).LanguageServerArguments(debug: true);
        Assert.Equal(["--log-format", "text", "--debug", "lsp", "serve"], args);
    }
}
