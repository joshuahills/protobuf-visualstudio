using Protobuf.VisualStudio.Services;
using Protobuf.VisualStudio.Tests.Infrastructure;

namespace Protobuf.VisualStudio.Tests.Integration;

/// <summary>Format on save's formatter with the real buf.</summary>
[Trait("Category", "Integration")]
public sealed class ProtoFormatterIntegrationTests
{
    [Fact]
    public async Task Formats_an_unsaved_buffer()
    {
        var buf = await RealBuf.GetAsync();

        var formatted = await ProtoFormatter.FormatAsync(buf.Path, "syntax=\"proto3\";package demo;message   A{string  name=1;}\n", TestContext.Current.CancellationToken);

        Assert.NotNull(formatted);
        Assert.Contains("message A {", formatted, StringComparison.Ordinal);
        Assert.Contains("  string name = 1;", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_CRLF_documents_CRLF()
    {
        var buf = await RealBuf.GetAsync();

        var formatted = await ProtoFormatter.FormatAsync(buf.Path, "syntax = \"proto3\";\r\nmessage   A {}\r\n", TestContext.Current.CancellationToken);

        Assert.NotNull(formatted);
        Assert.DoesNotContain("\n", formatted.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("message A {}\r\n", formatted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("syntax = \"proto3\"; message A { strin g x = 1 }")]
    [InlineData("syntax = \"proto3\"; message A { int32 x = 1;")]
    [InlineData("syntax = \"proto3\"; message A { int32 x = 1 }")]
    [InlineData("syntax = \"proto3\"; service S { rpc Get(A) returns ( }")]
    public async Task Half_typed_code_is_left_alone(string text)
    {
        // buf format alone would exit 0 and reflow these.
        var buf = await RealBuf.GetAsync();

        Assert.Null(await ProtoFormatter.FormatAsync(buf.Path, text, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unresolvable_imports_and_types_do_not_block_formatting()
    {
        var buf = await RealBuf.GetAsync();
        const string Text = "syntax = \"proto3\";\nimport \"acme/common/v1/location.proto\";\nmessage   A { acme.common.v1.Location l = 1; }\n";

        var formatted = await ProtoFormatter.FormatAsync(buf.Path, Text, TestContext.Current.CancellationToken);

        Assert.NotNull(formatted);
        Assert.Contains("message A {", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Buf_format_works_without_a_diff_on_PATH()
    {
        // buf format execs "diff" internally; most Windows machines don't have one on PATH.
        var buf = await RealBuf.GetAsync();
        using var temp = new TempDirectory();
        temp.File("buffer.proto", "syntax = \"proto3\";\nmessage   A {}\n");

        var info = BufCli.CreateStartInfo(buf.Path, ["format", "buffer.proto"], temp.Path);
        info.Environment["PATH"] = string.Empty;
        var diff = DiffTool.FindDirectory(string.Empty, DiffTool.DefaultCandidates());
        Assert.SkipWhen(diff is null, "No Git (for Windows or Visual Studio's) on this machine to borrow diff.exe from.");
        BufCli.AddDiffToPath(info, diff);

        using var process = System.Diagnostics.Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, stderr);
        Assert.Contains("message A {}", await stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sample_is_already_formatted()
    {
        var buf = await RealBuf.GetAsync();
        var path = Path.Combine(RealBuf.WeatherSample, "proto", "acme", "common", "v1", "location.proto");
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        var formatted = await ProtoFormatter.FormatAsync(buf.Path, text, TestContext.Current.CancellationToken);

        Assert.Null(ProtoFormatter.MinimalReplacement(text, formatted!));
    }
}
