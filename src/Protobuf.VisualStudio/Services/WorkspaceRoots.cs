namespace Protobuf.VisualStudio.Services;

/// <summary>Finds the Buf workspaces (folders with buf.yaml or buf.work.yaml) under a solution folder.</summary>
internal static class WorkspaceRoots
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".vscode", "bin", "obj", "node_modules", "packages", "TestResults", "worktrees",
    };

    /// <summary>
    /// Returns the outermost folders under <paramref name="root"/> that contain buf.work.yaml or buf.yaml. A v2
    /// buf.yaml (or a v1 buf.work.yaml) at the top covers the modules beneath it, so nested configs are dropped.
    /// When there is no Buf config at all the solution folder itself is returned, which is how buf behaves too.
    /// </summary>
    public static IReadOnlyList<string> Find(string root, int maxDepth = 8)
    {
        List<string> found = [];
        Walk(new DirectoryInfo(root), 0, maxDepth, found);
        return found.Count == 0 ? [root] : found;
    }

    /// <summary>
    /// The Buf workspace a file or folder belongs to, for running a command on just that path: the outermost
    /// ancestor with buf.yaml or buf.work.yaml (matching <see cref="Find"/>), not looking above the git repository
    /// root. Without any Buf config it falls back to <paramref name="solutionDirectory"/> when the path is inside it,
    /// else the path's own folder.
    /// </summary>
    public static string ForPath(string path, string? solutionDirectory)
    {
        var full = Path.GetFullPath(path);
        var start = Directory.Exists(full) ? new DirectoryInfo(full) : new DirectoryInfo(Path.GetDirectoryName(full)!);

        string? outermost = null;
        for (var dir = start; dir is not null; dir = dir.Parent)
        {
            if (HasConfig(dir.FullName))
            {
                outermost = dir.FullName;
            }

            // .git is a folder in a normal clone and a file in worktrees and submodules.
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                break;
            }
        }

        if (outermost is not null)
        {
            return outermost;
        }

        if (solutionDirectory is not null && IsUnder(full, solutionDirectory))
        {
            return Path.GetFullPath(solutionDirectory);
        }

        return start.FullName;
    }

    /// <summary>The <c>--path</c> argument for <paramref name="path"/>, relative to the workspace, with forward slashes.</summary>
    public static string RelativeArgument(string workspaceRoot, string path)
    {
        var relative = Path.GetRelativePath(workspaceRoot, Path.GetFullPath(path)).Replace('\\', '/');
        return relative == "." ? "." : relative.TrimEnd('/');
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="directory"/> or inside it.</summary>
    public static bool IsUnder(string path, string directory)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasConfig(string directory) =>
        File.Exists(Path.Combine(directory, "buf.work.yaml")) || File.Exists(Path.Combine(directory, "buf.yaml"));

    private static void Walk(DirectoryInfo directory, int depth, int maxDepth, List<string> found)
    {
        if (File.Exists(Path.Combine(directory.FullName, "buf.work.yaml")) || File.Exists(Path.Combine(directory.FullName, "buf.yaml")))
        {
            found.Add(directory.FullName);
            return;
        }

        if (depth >= maxDepth)
        {
            return;
        }

        IEnumerable<DirectoryInfo> children;
        try
        {
            children = directory.EnumerateDirectories();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var child in children)
        {
            if (SkippedDirectories.Contains(child.Name) || child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            Walk(child, depth + 1, maxDepth, found);
        }
    }
}
