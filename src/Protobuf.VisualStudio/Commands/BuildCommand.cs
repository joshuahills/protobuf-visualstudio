using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class BuildCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Build")
    {
        TooltipText = "Run 'buf build' in each Buf workspace of the solution and report compile errors in the Error List.",
        Icon = new(ImageMoniker.KnownValues.BuildSolution, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunCheckAsync(BufCheck.Fixed(BufCheckKind.Build, "Build", "build", "--error-format", "json"));
        return Task.CompletedTask;
    }
}
