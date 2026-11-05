using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class LintCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Lint")
    {
        TooltipText = "Run 'buf lint' in each Buf workspace of the solution and list the findings in the Error List.",
        Icon = new(ImageMoniker.KnownValues.CodeInformationRule, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunCheckAsync(BufCheck.Fixed(BufCheckKind.Lint, "Lint", "lint", "--error-format", "json"));
        return Task.CompletedTask;
    }
}
