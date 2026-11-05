using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

/// <summary>Solution Explorer: lint every .proto file under a folder or project.</summary>
[VisualStudioContribution]
internal sealed class LintFolderCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Lint Protos with Buf")
    {
        TooltipText = "Run 'buf lint' on the .proto files in this folder and list the findings in the Error List.",
        Icon = new(ImageMoniker.KnownValues.CodeInformationRule, IconSettings.IconAndText),
        Placements = [SolutionExplorerMenus.FolderNode, SolutionExplorerMenus.ProjectNode],
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        if (await SolutionExplorerMenus.SelectedPathAsync(context, cancellationToken) is not { } path)
        {
            return;
        }

        // A project node selects the project file; lint the folder it lives in.
        var folder = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        _ = service.RunOnPathAsync($"Lint {Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))}", folder, BufPathCommands.Lint, BufCheckKind.Lint);
    }
}
