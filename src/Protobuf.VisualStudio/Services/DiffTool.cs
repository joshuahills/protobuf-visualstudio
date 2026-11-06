namespace Protobuf.VisualStudio.Services;

/// <summary>
/// <c>buf format</c> always shells out to a <c>diff</c> program to compare the original and formatted files (even
/// without <c>--diff</c>), and fails with <c>exec: "diff": executable file not found</c> when there isn't one on PATH,
/// which is the norm on Windows. Git for Windows and the Git bundled with Visual Studio both ship a <c>diff.exe</c>,
/// so when PATH has none, buf gets one of those directories appended to its PATH.
/// </summary>
internal static class DiffTool
{
    private const string BundledGit = @"Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\usr\bin";

    private static readonly Lazy<string?> Cached = new(() => FindDirectory(Environment.GetEnvironmentVariable("PATH"), DefaultCandidates()));

    /// <summary>The directory to append to buf's PATH, or null when PATH already has a diff (or none was found).</summary>
    public static string? DirectoryToAdd => Cached.Value;

    /// <summary>
    /// Null when <paramref name="pathVariable"/> already finds a diff. Otherwise the first directory with a
    /// <c>diff.exe</c>: Git's <c>usr\bin</c> next to a <c>git.exe</c> on PATH, then <paramref name="candidates"/>.
    /// </summary>
    public static string? FindDirectory(string? pathVariable, IEnumerable<string> candidates)
    {
        var pathDirectories = (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim('"'))
            .ToList();

        if (pathDirectories.Any(HasDiff))
        {
            return null;
        }

        // Git for Windows puts git.exe in Git\cmd (the usual PATH entry) or Git\mingw64\bin; diff is in Git\usr\bin.
        var nextToGit = pathDirectories
            .Where(d => SafeExists(Path.Combine(d, "git.exe")))
            .SelectMany(d => new[] { Path.Combine(d, "..", "usr", "bin"), Path.Combine(d, "..", "..", "usr", "bin") });

        return nextToGit.Concat(candidates).Where(HasDiff).Select(Path.GetFullPath).FirstOrDefault();
    }

    /// <summary>Default Git for Windows installs, then the Git that ships with each Visual Studio install.</summary>
    public static IEnumerable<string> DefaultCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "Git", "usr", "bin");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "usr", "bin");

        // Set in processes Visual Studio starts: ...\Common7\IDE\.
        if (Environment.GetEnvironmentVariable("VSAPPIDDIR") is { Length: > 0 } ideDir)
        {
            yield return Path.Combine(ideDir, "..", "..", BundledGit);
        }

        var vsRoot = Path.Combine(programFiles, "Microsoft Visual Studio");
        string[] installs;
        try
        {
            // <root>\<version>\<edition>, e.g. ...\18\Enterprise.
            installs = Directory.Exists(vsRoot)
                ? Directory.GetDirectories(vsRoot).SelectMany(Directory.GetDirectories).OrderDescending(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            installs = [];
        }

        foreach (var install in installs)
        {
            yield return Path.Combine(install, BundledGit);
        }
    }

    private static bool HasDiff(string directory) => SafeExists(Path.Combine(directory, "diff.exe"));

    private static bool SafeExists(string file)
    {
        try
        {
            return File.Exists(file);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
