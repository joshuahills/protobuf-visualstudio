using System.Text.RegularExpressions;

namespace Protobuf.VisualStudio.Services;

/// <summary>
/// Command lines for running buf on one file or folder. buf is picky about how a path is passed: the workspace
/// itself takes no <c>--path</c> ("given input is equal to a value of --path"), a module root must be the input
/// ("module was specified with --path, specify this module path directly as an input"), and anything else is a
/// <c>--path</c> filter.
/// </summary>
internal static partial class BufPathCommands
{
    public static IReadOnlyList<string> Lint(string workspaceRoot, string relativePath) =>
        ["lint", .. Target(workspaceRoot, relativePath), "--error-format", "json"];

    /// <summary>buf format takes the file as its input, and -w rewrites it in place.</summary>
    public static IReadOnlyList<string> Format(string workspaceRoot, string relativePath) => ["format", "-w", relativePath];

    /// <summary>The input and <c>--path</c> arguments that select <paramref name="relativePath"/>.</summary>
    public static IReadOnlyList<string> Target(string workspaceRoot, string relativePath)
    {
        if (relativePath is "." or "")
        {
            return [];
        }

        var modules = ModuleRoots(ReadOrNull(Path.Combine(workspaceRoot, "buf.yaml")), ReadOrNull(Path.Combine(workspaceRoot, "buf.work.yaml")));
        return modules.Contains(relativePath.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            ? [relativePath]
            : ["--path", relativePath];
    }

    /// <summary>
    /// Module directories declared by a v2 buf.yaml (<c>modules: - path: x</c>) or a v1 buf.work.yaml
    /// (<c>directories: - x</c>), normalised to forward slashes without a leading <c>./</c>.
    /// </summary>
    public static IReadOnlyList<string> ModuleRoots(string? bufYaml, string? bufWorkYaml)
    {
        List<string> roots = [];
        if (bufYaml is not null)
        {
            roots.AddRange(ListItems(bufYaml, "modules").Select(item => ModulePath().Match(item)).Where(m => m.Success).Select(m => m.Groups[1].Value));
        }

        if (bufWorkYaml is not null)
        {
            roots.AddRange(ListItems(bufWorkYaml, "directories"));
        }

        return roots.Select(Normalise).Where(r => r.Length > 0 && r != ".").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The raw text after "- " for each item of a top-level YAML list.</summary>
    private static IEnumerable<string> ListItems(string yaml, string key)
    {
        var inList = false;
        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var content = line.Split('#')[0];
            if (content.Trim().Length == 0)
            {
                continue;
            }

            var indented = char.IsWhiteSpace(content[0]) || content.TrimStart().StartsWith('-');
            if (!indented)
            {
                inList = content.TrimEnd() == key + ":";
                continue;
            }

            var trimmed = content.Trim();
            if (inList && trimmed.StartsWith('-'))
            {
                yield return trimmed[1..].Trim();
            }
        }
    }

    private static string Normalise(string path)
    {
        var p = path.Trim().Trim('"', '\'').Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        return p == "." ? p : p.TrimEnd('/');
    }

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^path:\s*(\S+)")]
    private static partial Regex ModulePath();
}
