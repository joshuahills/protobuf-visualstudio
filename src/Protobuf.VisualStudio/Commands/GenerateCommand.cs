using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class GenerateCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Generate")
    {
        TooltipText = "Run 'buf generate' (uses buf.gen.yaml) in each Buf workspace of the solution.",
        Icon = new(ImageMoniker.KnownValues.GenerateFile, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunInWorkspacesAsync("Generate", ["generate"]);
        return Task.CompletedTask;
    }
}
