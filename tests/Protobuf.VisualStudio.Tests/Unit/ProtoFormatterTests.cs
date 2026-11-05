using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Tests.Unit;

public class ProtoFormatterTests
{
    [Fact]
    public void Equal_texts_need_no_edit()
    {
        Assert.Null(ProtoFormatter.MinimalReplacement("message A {}\n", "message A {}\n"));
    }

    [Theory]
    [InlineData("message   A {}", "message A {}")]
    [InlineData("a", "abc")]
    [InlineData("abc", "a")]
    [InlineData("", "syntax = \"proto3\";\n")]
    [InlineData("x\r\ny  z\r\n", "x\r\ny z\r\n")]
    [InlineData("aaaa", "aa")]
    [InlineData("int32 a=1;\nint32 b=2;\n", "int32 a = 1;\nint32 b = 2;\n")]
    public void Applying_the_replacement_gives_the_new_text(string before, string after)
    {
        var edit = ProtoFormatter.MinimalReplacement(before, after);

        Assert.NotNull(edit);
        var applied = before.Remove(edit.Value.Start, edit.Value.Length).Insert(edit.Value.Start, edit.Value.NewText);
        Assert.Equal(after, applied);
    }

    [Fact]
    public void Only_the_changed_middle_is_replaced()
    {
        var edit = ProtoFormatter.MinimalReplacement("header\nmessage   A {}\nfooter\n", "header\nmessage A {}\nfooter\n");

        Assert.Equal(new TextReplacement(15, 2, string.Empty), edit);
    }

    [Fact]
    public void A_CRLF_pair_is_never_split()
    {
        // Inserting a line before a CRLF: a naive prefix would end between \r and \n.
        var before = "a\r\nb\r\n";
        var after = "a\r\n\r\nb\r\n";

        var edit = ProtoFormatter.MinimalReplacement(before, after)!.Value;

        Assert.False(edit.Start > 0 && before[edit.Start - 1] == '\r' && before[edit.Start] == '\n');
        Assert.Equal(after, before.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText));
    }

    [Fact]
    public void A_surrogate_pair_is_never_split()
    {
        var before = "// 😀a\n";
        var after = "// 😁a\n";

        var edit = ProtoFormatter.MinimalReplacement(before, after)!.Value;

        Assert.False(char.IsLowSurrogate(before[edit.Start]));
        Assert.Equal(after, before.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.NewText));
    }

    [Theory]
    [InlineData("a\nb\n", "x\r\ny\r\n", "a\r\nb\r\n")]
    [InlineData("a\nb\n", "x\ny\n", "a\nb\n")]
    [InlineData("a\r\nb\n", "x\ny\n", "a\nb\n")]
    public void Keeps_the_document_line_endings(string formatted, string original, string expected)
    {
        Assert.Equal(expected, ProtoFormatter.MatchLineEndings(formatted, original));
    }

    // Messages captured from buf 1.75.0 building a lone file.
    [Theory]
    [InlineData("imported file does not exist", true)]
    [InlineData("cannot find `acme.common.v1.Location` in this scope", true)]
    [InlineData("missing message field tag in declaration", false)]
    [InlineData("unexpected `}` after definition", false)]
    [InlineData("encountered unmatched `{` delimiter", false)]
    [InlineData("unexpected `=` in definition", false)]
    [InlineData("missing method return type in service method", false)]
    [InlineData("field number `1` used more than once", false)]
    [InlineData("something new in a future buf", false)]
    public void Formats_only_when_every_error_is_an_unresolved_import_or_name(string message, bool safe)
    {
        BufAnnotation[] errors =
        [
            new(@"C:\t\buffer.proto", 1, 1, 1, 2, BufAnnotation.CompileType, "imported file does not exist"),
            new(@"C:\t\buffer.proto", 2, 1, 2, 2, BufAnnotation.CompileType, message),
        ];

        Assert.Equal(safe, ProtoFormatter.SafeToFormat(errors));
    }

    [Fact]
    public void A_clean_build_is_safe_to_format()
    {
        Assert.True(ProtoFormatter.SafeToFormat([]));
    }
}
