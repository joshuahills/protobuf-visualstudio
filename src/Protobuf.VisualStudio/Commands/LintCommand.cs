using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class LintCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Lint")
    {
        TooltipText = "Run 'buf lint' in each Buf workspace of the solution.",
        Icon = new(ImageMoniker.KnownValues.CodeInformationRule, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunInWorkspacesAsync("Lint", ["lint", "--error-format", "msvs"]);
        return Task.CompletedTask;
    }
}
