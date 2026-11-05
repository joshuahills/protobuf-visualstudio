using System.Text;

namespace Protobuf.VisualStudio.Services;

/// <summary>A single replacement that turns one text into another.</summary>
internal readonly record struct TextReplacement(int Start, int Length, string NewText);

/// <summary>Formats .proto text with <c>buf format</c> and works out the smallest edit to apply it.</summary>
internal static class ProtoFormatter
{
    /// <summary>
    /// Formats <paramref name="text"/> (an unsaved buffer) with buf, or returns null to leave the document alone.
    /// buf can't read a .proto from stdin, so the text goes to a temp file. <c>buf format</c> happily "formats"
    /// code with syntax errors (exit 0, mangled layout), so the file is compiled first and formatting is skipped
    /// unless the only errors are ones a lone temp file always has: imports and names it can't resolve.
    /// </summary>
    public static async Task<string?> FormatAsync(string bufPath, string text, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BufForVisualStudio", "format", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "buffer.proto");
            await File.WriteAllTextAsync(file, text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            var build = await BufCli.RunAsync(bufPath, ["build", "buffer.proto", "--error-format", "json"], directory, cancellationToken).ConfigureAwait(false);
            if (!SafeToFormat(BufAnnotation.ParseOutput(build, directory)))
            {
                return null;
            }

            var result = await BufCli.RunAsync(bufPath, ["format", "buffer.proto"], directory, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? MatchLineEndings(result.StandardOutput, text) : null;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// True when compiling the lone buffer only failed to resolve imports or names, which is expected outside the
    /// workspace. Anything else may be a syntax error (or is unknown), so formatting is not safe.
    /// </summary>
    public static bool SafeToFormat(IEnumerable<BufAnnotation> buildErrors) =>
        buildErrors.All(e => e.Message == "imported file does not exist"
            || (e.Message.StartsWith("cannot find `", StringComparison.Ordinal) && e.Message.EndsWith("` in this scope", StringComparison.Ordinal)));

    /// <summary>buf writes LF; keep the document's own line endings (CRLF if it uses any).</summary>
    public static string MatchLineEndings(string formatted, string original)
    {
        var lf = formatted.Replace("\r\n", "\n", StringComparison.Ordinal);
        return original.Contains("\r\n", StringComparison.Ordinal) ? lf.Replace("\n", "\r\n", StringComparison.Ordinal) : lf;
    }

    /// <summary>
    /// The smallest single replacement (common prefix and suffix trimmed) that turns <paramref name="before"/> into
    /// <paramref name="after"/>, or null when they're equal. Keeps the caret and undo history stable.
    /// </summary>
    public static TextReplacement? MinimalReplacement(string before, string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return null;
        }

        var prefix = 0;
        var max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < max - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        // Never split a CRLF pair or a surrogate pair across the edit boundary.
        while (prefix > 0 && (IsSecondHalf(before, prefix) || IsSecondHalf(after, prefix)))
        {
            prefix--;
        }

        while (suffix > 0 && (IsSecondHalf(before, before.Length - suffix) || IsSecondHalf(after, after.Length - suffix)))
        {
            suffix--;
        }

        return new TextReplacement(prefix, before.Length - prefix - suffix, after.Substring(prefix, after.Length - prefix - suffix));
    }

    private static bool IsSecondHalf(string text, int index) =>
        index > 0 && index < text.Length &&
        ((text[index - 1] == '\r' && text[index] == '\n') || (char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index])));
}
