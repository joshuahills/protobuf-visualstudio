namespace Protobuf.VisualStudio.Services;

/// <summary>The buf commands whose findings go to the Error List. Each has its own diagnostics provider.</summary>
internal enum BufCheckKind
{
    Build,
    Lint,
    Breaking,
}

internal enum BufSeverity
{
    Error,
    Warning,
}

/// <summary>
/// A buf command run in every workspace whose JSON findings are published to the Error List.
/// <paramref name="Arguments"/> builds the command line for one workspace root, or returns null to skip it
/// (after logging why).
/// </summary>
internal sealed record BufCheck(BufCheckKind Kind, string Title, Func<string, CancellationToken, Task<IReadOnlyList<string>?>> Arguments)
{
    /// <summary>Exit code buf uses when the command ran and reported findings (as opposed to failing).</summary>
    public const int FindingsExitCode = 100;

    public static BufCheck Fixed(BufCheckKind kind, string title, params string[] arguments) =>
        new(kind, title, (_, _) => Task.FromResult<IReadOnlyList<string>?>(arguments));
}

/// <summary>
/// An Error List entry in Visual Studio's terms (0-based range, exclusive end column), independent of the VS SDK
/// types so it can be unit tested; <see cref="BufDiagnosticsPublisher"/> converts it.
/// </summary>
internal sealed record BufDiagnostic(
    string Path,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string Message,
    string Code,
    BufSeverity Severity,
    string ProviderName,
    string? HelpLink);

/// <summary>Maps buf annotations to Error List entries.</summary>
internal static class BufDiagnosticMapper
{
    public static string ProviderName(BufCheckKind kind) => $"Buf {kind}";

    public static BufSeverity Severity(BufAnnotation annotation, BufCheckKind kind) =>
        annotation.Type == BufAnnotation.CompileType || kind == BufCheckKind.Breaking
            ? BufSeverity.Error
            : BufSeverity.Warning;

    /// <summary>The rule's documentation page; compile errors have none.</summary>
    public static string? HelpLink(BufAnnotation annotation, BufCheckKind kind)
    {
        if (annotation.Type is BufAnnotation.CompileType or "UNKNOWN")
        {
            return null;
        }

        var anchor = annotation.Type.ToLowerInvariant();
        return kind == BufCheckKind.Breaking
            ? $"https://buf.build/docs/breaking/rules/#{anchor}"
            : $"https://buf.build/docs/lint/rules/#{anchor}";
    }

    public static BufDiagnostic ToDiagnostic(BufAnnotation annotation, BufCheckKind kind) => new(
        annotation.Path,
        // buf is 1-based with an exclusive end column; Visual Studio ranges are 0-based.
        annotation.StartLine - 1,
        annotation.StartColumn - 1,
        annotation.EndLine - 1,
        annotation.EndColumn - 1,
        annotation.Message,
        annotation.Type,
        Severity(annotation, kind),
        ProviderName(kind),
        HelpLink(annotation, kind));
}
