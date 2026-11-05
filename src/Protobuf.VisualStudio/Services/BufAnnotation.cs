using System.Text.Json;

namespace Protobuf.VisualStudio.Services;

/// <summary>
/// One finding from <c>buf build</c>, <c>buf lint</c> or <c>buf breaking</c> run with <c>--error-format json</c>.
/// Lines and columns are 1-based as buf reports them; <see cref="EndColumn"/> is exclusive.
/// </summary>
/// <param name="Path">Absolute path of the file (buf reports it relative to the directory it ran in).</param>
/// <param name="Type">Rule id such as <c>FIELD_LOWER_SNAKE_CASE</c>, or <c>COMPILE</c> for compiler errors.</param>
internal sealed record BufAnnotation(string Path, int StartLine, int StartColumn, int EndLine, int EndColumn, string Type, string Message)
{
    public const string CompileType = "COMPILE";

    /// <summary>
    /// Parses buf's JSON-lines output. Lines that are not JSON objects (progress, warnings) are skipped, as are
    /// objects without a path or message. Relative paths resolve against <paramref name="workingDirectory"/>.
    /// </summary>
    public static IReadOnlyList<BufAnnotation> ParseJsonLines(string output, string workingDirectory)
    {
        List<BufAnnotation> annotations = [];
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 2 || line[0] != '{')
            {
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                var path = String(root, "path");
                var message = String(root, "message");
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(message))
                {
                    continue;
                }

                var startLine = Math.Max(1, Int(root, "start_line"));
                var startColumn = Math.Max(1, Int(root, "start_column"));
                var endLine = Math.Max(startLine, Int(root, "end_line"));
                var endColumn = Int(root, "end_column");
                if (endLine == startLine && endColumn <= startColumn)
                {
                    endColumn = startColumn + 1;
                }

                annotations.Add(new BufAnnotation(
                    ResolvePath(path, workingDirectory),
                    startLine,
                    startColumn,
                    endLine,
                    endColumn,
                    String(root, "type") is { Length: > 0 } type ? type : "UNKNOWN",
                    message));
            }
            catch (JsonException)
            {
                // Not a JSON annotation; buf also prints plain text in some failure modes.
            }
        }

        return annotations;
    }

    /// <summary>Formats the annotation as a clickable line for the Output pane: <c>path(line,col): error TYPE: message</c>.</summary>
    public string ToMsBuildLine(string severity) => $"{this.Path}({this.StartLine},{this.StartColumn}): {severity} {this.Type}: {this.Message}";

    private static string ResolvePath(string path, string workingDirectory)
    {
        var normalized = path.Replace('/', System.IO.Path.DirectorySeparatorChar);
        return System.IO.Path.IsPathRooted(normalized)
            ? System.IO.Path.GetFullPath(normalized)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDirectory, normalized));
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
