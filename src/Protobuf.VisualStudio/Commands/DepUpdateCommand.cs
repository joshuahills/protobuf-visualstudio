using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class DepUpdateCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Update Dependencies")
    {
        TooltipText = "Run 'buf dep update' to update buf.lock in each Buf workspace.",
        Icon = new(ImageMoniker.KnownValues.Refresh, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunInWorkspacesAsync("Update Dependencies", ["dep", "update"]);
        return Task.CompletedTask;
    }
}
