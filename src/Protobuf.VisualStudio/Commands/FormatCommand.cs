using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class FormatCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Format All Proto Files")
    {
        TooltipText = "Rewrite every .proto file in each Buf workspace with 'buf format -w'.",
        Icon = new(ImageMoniker.KnownValues.FormatDocument, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunInWorkspacesAsync("Format All Proto Files", ["format", "-w"]);
        return Task.CompletedTask;
    }
}
