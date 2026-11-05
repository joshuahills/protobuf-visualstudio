using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace Protobuf.VisualStudio.Commands;

/// <summary>Solution Explorer context menus (guidSHLMainMenu IDs from the VS SDK's vsshlids.h).</summary>
internal static class SolutionExplorerMenus
{
    private static readonly Guid ShellMainMenu = new("d309f791-903f-11d0-9efc-00a0c90f57e3");

    /// <summary>IDM_VS_CTXT_ITEMNODE: a file in a project.</summary>
    public static CommandPlacement ItemNode { get; } = CommandPlacement.VsctParent(ShellMainMenu, id: 0x0430, priority: 0x0600);

    /// <summary>IDM_VS_CTXT_FOLDERNODE: a folder in a project.</summary>
    public static CommandPlacement FolderNode { get; } = CommandPlacement.VsctParent(ShellMainMenu, id: 0x0431, priority: 0x0600);

    /// <summary>IDM_VS_CTXT_PROJNODE: a project.</summary>
    public static CommandPlacement ProjectNode { get; } = CommandPlacement.VsctParent(ShellMainMenu, id: 0x0402, priority: 0x0600);

    /// <summary>Only show on .proto files.</summary>
    public static ActivationConstraint ProtoFileSelected { get; } =
        ActivationConstraint.ClientContext(ClientContextKey.Shell.ActiveSelectionFileName, @"\.proto$");

    /// <summary>The selected file or folder as a local path, or null.</summary>
    public static async Task<string?> SelectedPathAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var uri = await context.GetSelectedPathAsync(cancellationToken).ConfigureAwait(false);
        return uri is { IsFile: true } ? uri.LocalPath : null;
    }
}
