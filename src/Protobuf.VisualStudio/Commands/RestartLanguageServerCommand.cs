using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class RestartLanguageServerCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Restart Language Server")
    {
        TooltipText = "Stop the Buf language server and start it again, picking up new settings or a new buf.exe.",
        Icon = new(ImageMoniker.KnownValues.Restart, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        service.RequestLanguageServerRestart();
        return Task.CompletedTask;
    }
}
