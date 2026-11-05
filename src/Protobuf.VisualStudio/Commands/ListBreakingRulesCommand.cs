using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class ListBreakingRulesCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("List Breaking Change Rules")
    {
        TooltipText = "Run 'buf config ls-breaking-rules' and show the rules in the Buf output pane.",
        Icon = new(ImageMoniker.KnownValues.BulletList, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunInWorkspacesAsync("List Breaking Change Rules", ["config", "ls-breaking-rules"]);
        return Task.CompletedTask;
    }
}
