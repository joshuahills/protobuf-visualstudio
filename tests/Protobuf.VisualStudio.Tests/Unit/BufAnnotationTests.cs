using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Tests.Unit;

public class BufAnnotationTests
{
    private const string Root = @"C:\repo\weather";

    // Captured from buf 1.75.0 on samples/weather.
    private const string LintLine = """{"path":"proto\\acme\\weather\\v1\\weather.proto","start_line":42,"start_column":10,"end_line":42,"end_column":22,"type":"FIELD_LOWER_SNAKE_CASE","message":"Field name \"windSpeedKph\" should be lower_snake_case, such as \"wind_speed_kph\"."}""";
    private const string CompileLine = """{"path":"proto\\acme\\weather\\v1\\weather.proto","start_line":23,"start_column":3,"end_line":23,"end_column":14,"type":"COMPILE","message":"cannot find `Observashun` in this scope"}""";
    private const string BreakingLine = """{"path":"proto\\acme\\weather\\v1\\weather.proto","start_line":37,"start_column":1,"end_line":42,"end_column":2,"type":"FIELD_NO_DELETE","message":"Previously present field \"2\" with name \"temperature_celsius\" on message \"Observation\" was deleted."}""";

    [Fact]
    public void Parses_a_lint_annotation()
    {
        var annotation = Assert.Single(BufAnnotation.ParseJsonLines(LintLine, Root));

        Assert.Equal(@"C:\repo\weather\proto\acme\weather\v1\weather.proto", annotation.Path);
        Assert.Equal((42, 10, 42, 22), (annotation.StartLine, annotation.StartColumn, annotation.EndLine, annotation.EndColumn));
        Assert.Equal("FIELD_LOWER_SNAKE_CASE", annotation.Type);
        Assert.StartsWith("Field name \"windSpeedKph\"", annotation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_several_lines_and_skips_noise()
    {
        var output = string.Join("\r\n", "WARN something happened", LintLine, "", "not json {", CompileLine, BreakingLine, "{broken json");

        var annotations = BufAnnotation.ParseJsonLines(output, Root);

        Assert.Equal(["FIELD_LOWER_SNAKE_CASE", BufAnnotation.CompileType, "FIELD_NO_DELETE"], annotations.Select(a => a.Type));
    }

    [Fact]
    public void Multi_line_ranges_are_kept()
    {
        var annotation = Assert.Single(BufAnnotation.ParseJsonLines(BreakingLine, Root));
        Assert.Equal((37, 1, 42, 2), (annotation.StartLine, annotation.StartColumn, annotation.EndLine, annotation.EndColumn));
    }

    [Fact]
    public void Forward_slashes_and_absolute_paths_are_normalised()
    {
        var output = """
            {"path":"proto/a.proto","start_line":1,"start_column":1,"end_line":1,"end_column":5,"type":"X","message":"m"}
            {"path":"D:\\other\\b.proto","start_line":1,"start_column":1,"end_line":1,"end_column":5,"type":"X","message":"m"}
            """;

        var paths = BufAnnotation.ParseJsonLines(output, Root).Select(a => a.Path).ToArray();

        Assert.Equal([@"C:\repo\weather\proto\a.proto", @"D:\other\b.proto"], paths);
    }

    [Theory]
    [InlineData("""{"path":"a.proto","start_line":5,"start_column":3,"type":"X","message":"no end"}""", 5, 3, 5, 4)]
    [InlineData("""{"path":"a.proto","start_line":0,"start_column":0,"end_line":0,"end_column":0,"type":"X","message":"file level"}""", 1, 1, 1, 2)]
    public void Missing_or_empty_ranges_become_one_character(string json, int startLine, int startColumn, int endLine, int endColumn)
    {
        var annotation = Assert.Single(BufAnnotation.ParseJsonLines(json, Root));
        Assert.Equal((startLine, startColumn, endLine, endColumn), (annotation.StartLine, annotation.StartColumn, annotation.EndLine, annotation.EndColumn));
    }

    [Theory]
    [InlineData("""{"start_line":1,"type":"X","message":"no path"}""")]
    [InlineData("""{"path":"a.proto","type":"X"}""")]
    [InlineData("""{"path":"a.proto","message":""}""")]
    public void Objects_without_path_or_message_are_ignored(string json)
    {
        Assert.Empty(BufAnnotation.ParseJsonLines(json, Root));
    }

    [Fact]
    public void A_missing_type_becomes_UNKNOWN()
    {
        var annotation = Assert.Single(BufAnnotation.ParseJsonLines("""{"path":"a.proto","message":"m"}""", Root));
        Assert.Equal("UNKNOWN", annotation.Type);
    }

    [Fact]
    public void Formats_a_clickable_output_line()
    {
        var annotation = Assert.Single(BufAnnotation.ParseJsonLines(CompileLine, Root));
        Assert.Equal(
            @"C:\repo\weather\proto\acme\weather\v1\weather.proto(23,3): error COMPILE: cannot find `Observashun` in this scope",
            annotation.ToMsBuildLine("error"));
    }
}
