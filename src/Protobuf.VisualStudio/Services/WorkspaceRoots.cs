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
