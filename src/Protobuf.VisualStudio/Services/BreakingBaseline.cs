namespace Protobuf.VisualStudio.Services;

/// <summary>The input <c>buf breaking --against</c> compares a workspace with, or why none could be found.</summary>
internal sealed record BreakingBaselineResult(string? Against, string? Error)
{
    public static BreakingBaselineResult Found(string against) => new(against, null);

    public static BreakingBaselineResult Failed(string error) => new(null, error);
}

/// <summary>
/// Works out what to compare against for <c>buf breaking</c>. A configured value is used verbatim (any buf input:
/// <c>.git#tag=v1.0.0</c>, <c>buf.build/acme/weather</c>, a directory). Otherwise the git default branch of the
/// repository that contains the workspace is used, with <c>subdir=</c> when the workspace isn't the repository root.
/// </summary>
internal static class BreakingBaseline
{
    /// <summary>Runs git and returns its exit code and trimmed stdout.</summary>
    public delegate Task<(int ExitCode, string Output)> GitRunner(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    public static async Task<BreakingBaselineResult> ResolveAsync(string workspaceRoot, string? configured, GitRunner git, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return BreakingBaselineResult.Found(configured.Trim());
        }

        var (topLevelExit, topLevel) = await git(workspaceRoot, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (topLevelExit != 0 || string.IsNullOrWhiteSpace(topLevel))
        {
            return BreakingBaselineResult.Failed(
                $"{workspaceRoot} is not in a git repository. Set Tools > Options > Buf > Breaking change baseline (for example buf.build/owner/module or a folder).");
        }

        var (refKind, refName) = await DefaultBranchAsync(workspaceRoot, git, cancellationToken).ConfigureAwait(false);
        if (refName is null)
        {
            return BreakingBaselineResult.Failed(
                $"Could not find a main or master branch, or origin/HEAD, in {topLevel}. Set Tools > Options > Buf > Breaking change baseline, for example .git#branch=develop.");
        }

        return BreakingBaselineResult.Found(FormatGitInput(workspaceRoot, topLevel, refKind, refName));
    }

    /// <summary>
    /// Builds a buf git input such as <c>../../.git#branch=main,subdir=samples/weather</c>, relative to
    /// <paramref name="workspaceRoot"/> (where buf runs), with forward slashes as buf expects.
    /// </summary>
    public static string FormatGitInput(string workspaceRoot, string repositoryRoot, string refKind, string refName)
    {
        var workspace = Path.GetFullPath(workspaceRoot);
        var repository = Path.GetFullPath(repositoryRoot.Replace('/', Path.DirectorySeparatorChar));

        var gitDir = Path.GetRelativePath(workspace, Path.Combine(repository, ".git")).Replace('\\', '/');
        var subdir = Path.GetRelativePath(repository, workspace).Replace('\\', '/');

        var input = $"{gitDir}#{refKind}={refName}";
        return subdir is "." or "" ? input : $"{input},subdir={subdir}";
    }

    /// <summary>
    /// Picks the branch to compare against: the local branch origin/HEAD points to, then local main or master, then
    /// the remote-tracking ref when only the remote has it (fresh clones of other branches).
    /// </summary>
    private static async Task<(string Kind, string? Name)> DefaultBranchAsync(string directory, GitRunner git, CancellationToken cancellationToken)
    {
        List<string> candidates = [];
        var (headExit, originHead) = await git(directory, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], cancellationToken).ConfigureAwait(false);
        if (headExit == 0 && originHead.StartsWith("origin/", StringComparison.Ordinal))
        {
            candidates.Add(originHead["origin/".Length..]);
        }

        candidates.AddRange(["main", "master"]);

        foreach (var branch in candidates.Distinct(StringComparer.Ordinal))
        {
            if (await RefExistsAsync(directory, $"refs/heads/{branch}", git, cancellationToken).ConfigureAwait(false))
            {
                return ("branch", branch);
            }
        }

        foreach (var branch in candidates.Distinct(StringComparer.Ordinal))
        {
            if (await RefExistsAsync(directory, $"refs/remotes/origin/{branch}", git, cancellationToken).ConfigureAwait(false))
            {
                return ("ref", $"refs/remotes/origin/{branch}");
            }
        }

        return ("branch", null);
    }

    private static async Task<bool> RefExistsAsync(string directory, string reference, GitRunner git, CancellationToken cancellationToken) =>
        (await git(directory, ["rev-parse", "--verify", "--quiet", reference], cancellationToken).ConfigureAwait(false)).ExitCode == 0;

    /// <summary>Runs git.exe from PATH.</summary>
    public static async Task<(int ExitCode, string Output)> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            var result = await BufCli.RunAsync("git", arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
            return (result.ExitCode, result.StandardOutput.Trim());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git is not installed or not on PATH.
            return (-1, string.Empty);
        }
    }
}
