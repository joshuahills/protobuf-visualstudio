using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Tests.Unit;

public class BufDiagnosticMapperTests
{
    private static readonly BufAnnotation Lint = new(@"C:\repo\a.proto", 42, 10, 42, 22, "FIELD_LOWER_SNAKE_CASE", "Field name should be lower_snake_case.");
    private static readonly BufAnnotation Compile = new(@"C:\repo\a.proto", 23, 3, 23, 14, BufAnnotation.CompileType, "cannot find `Observashun` in this scope");
    private static readonly BufAnnotation Breaking = new(@"C:\repo\a.proto", 37, 1, 42, 2, "FIELD_NO_DELETE", "Previously present field \"2\" was deleted.");

    [Fact]
    public void Converts_one_based_buf_ranges_to_zero_based_visual_studio_ranges()
    {
        var diagnostic = BufDiagnosticMapper.ToDiagnostic(Lint, BufCheckKind.Lint);

        Assert.Equal((41, 9, 41, 21), (diagnostic.StartLine, diagnostic.StartColumn, diagnostic.EndLine, diagnostic.EndColumn));
        Assert.Equal(@"C:\repo\a.proto", diagnostic.Path);
        Assert.Equal("Field name should be lower_snake_case.", diagnostic.Message);
        Assert.Equal("FIELD_LOWER_SNAKE_CASE", diagnostic.Code);
    }

    [Theory]
    [InlineData("Lint", false, "Warning")]
    [InlineData("Lint", true, "Error")]
    [InlineData("Build", true, "Error")]
    [InlineData("Breaking", false, "Error")]
    public void Compile_errors_and_breaking_changes_are_errors_and_lint_is_a_warning(string kindName, bool compile, string severity)
    {
        var kind = Enum.Parse<BufCheckKind>(kindName);
        var expected = Enum.Parse<BufSeverity>(severity);
        var annotation = compile ? Compile : kind == BufCheckKind.Breaking ? Breaking : Lint;
        Assert.Equal(expected, BufDiagnosticMapper.ToDiagnostic(annotation, kind).Severity);
    }

    [Fact]
    public void Help_links_point_at_the_rule_documentation()
    {
        Assert.Equal("https://buf.build/docs/lint/rules/#field_lower_snake_case", BufDiagnosticMapper.HelpLink(Lint, BufCheckKind.Lint));
        Assert.Equal("https://buf.build/docs/breaking/rules/#field_no_delete", BufDiagnosticMapper.HelpLink(Breaking, BufCheckKind.Breaking));
        Assert.Null(BufDiagnosticMapper.HelpLink(Compile, BufCheckKind.Build));
    }

    [Fact]
    public void Each_check_has_its_own_provider_so_results_replace_independently()
    {
        var names = Enum.GetValues<BufCheckKind>().Select(BufDiagnosticMapper.ProviderName).ToArray();

        Assert.Equal(["Buf Build", "Buf Lint", "Buf Breaking"], names);
        Assert.Equal("Buf Lint", BufDiagnosticMapper.ToDiagnostic(Lint, BufCheckKind.Lint).ProviderName);
    }

    [Fact]
    public async Task Fixed_checks_use_the_same_arguments_in_every_workspace()
    {
        var check = BufCheck.Fixed(BufCheckKind.Lint, "Lint", "lint", "--error-format", "json");

        Assert.Equal(["lint", "--error-format", "json"], await check.Arguments(@"C:\a", TestContext.Current.CancellationToken));
        Assert.Equal(["lint", "--error-format", "json"], await check.Arguments(@"C:\b", TestContext.Current.CancellationToken));
    }
}
