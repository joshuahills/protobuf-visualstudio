using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

/// <summary>Solution Explorer: format one .proto file on disk.</summary>
[VisualStudioContribution]
internal sealed class FormatFileCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Format with Buf")
    {
        TooltipText = "Rewrite this file with 'buf format -w'. To format an open document, use Edit > Advanced > Format Document instead.",
        Icon = new(ImageMoniker.KnownValues.FormatDocument, IconSettings.IconAndText),
        Placements = [SolutionExplorerMenus.ItemNode],
        VisibleWhen = SolutionExplorerMenus.ProtoFileSelected,
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        if (await SolutionExplorerMenus.SelectedPathAsync(context, cancellationToken) is { } path)
        {
            _ = service.RunOnPathAsync($"Format {Path.GetFileName(path)}", path, BufPathCommands.Format);
        }
    }
}
