using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

/// <summary>Solution Explorer: lint one .proto file.</summary>
[VisualStudioContribution]
internal sealed class LintFileCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Lint with Buf")
    {
        TooltipText = "Run 'buf lint' on this file and list the findings in the Error List.",
        Icon = new(ImageMoniker.KnownValues.CodeInformationRule, IconSettings.IconAndText),
        Placements = [SolutionExplorerMenus.ItemNode],
        VisibleWhen = SolutionExplorerMenus.ProtoFileSelected,
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        if (await SolutionExplorerMenus.SelectedPathAsync(context, cancellationToken) is { } path)
        {
            _ = service.RunOnPathAsync($"Lint {Path.GetFileName(path)}", path, BufPathCommands.Lint, BufCheckKind.Lint);
        }
    }
}
