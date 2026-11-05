using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class ClearResultsCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Clear Error List Results")
    {
        TooltipText = "Remove the Build, Lint and Breaking results that Tools > Buf put in the Error List. Live language server diagnostics are unaffected.",
        Icon = new(ImageMoniker.KnownValues.ClearWindowContent, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.ClearResultsAsync();
        return Task.CompletedTask;
    }
}
