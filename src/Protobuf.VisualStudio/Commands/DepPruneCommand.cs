using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class DepPruneCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Prune Dependencies")
    {
        TooltipText = "Run 'buf dep prune' to remove unused buf.lock entries in each Buf workspace.",
        Icon = new(ImageMoniker.KnownValues.CleanData, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunInWorkspacesAsync("Prune Dependencies", ["dep", "prune"]);
        return Task.CompletedTask;
    }
}
