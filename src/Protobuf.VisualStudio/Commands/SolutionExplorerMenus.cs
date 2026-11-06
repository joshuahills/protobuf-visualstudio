using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace Protobuf.VisualStudio.Commands;

/// <summary>Solution Explorer context menus (guidSHLMainMenu group IDs from the VS SDK's vsshlids.h).</summary>
internal static class SolutionExplorerMenus
{
    private static readonly Guid ShellMainMenu = new("d309f791-903f-11d0-9efc-00a0c911004f");

    // Commands must sit in a group inside a menu: parented straight to a context menu (IDM_VS_CTXT_ITEMNODE etc.)
    // they are registered but never shown.

    /// <summary>IDG_VS_CTXT_ITEM_OPEN: the Open / Open With group of a file's context menu.</summary>
    public static CommandPlacement ItemNode { get; } = CommandPlacement.VsctParent(ShellMainMenu, id: 0x0209, priority: 0x0600);

    /// <summary>IDG_VS_CTXT_FOLDER_EXPLORE: next to "Open Folder in File Explorer" on a folder.</summary>
    public static CommandPlacement FolderNode { get; } = CommandPlacement.VsctParent(ShellMainMenu, id: 0x0267, priority: 0x0600);

    /// <summary>IDG_VS_CTXT_PROJECT_BUILD: the Build / Rebuild / Clean group of a project.</summary>
    public static CommandPlacement ProjectNode { get; } = CommandPlacement.VsctParent(ShellMainMenu, id: 0x0206, priority: 0x0600);

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
