using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class ListLintRulesCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("List Lint Rules")
    {
        TooltipText = "Run 'buf config ls-lint-rules' and show the rules in the Buf output pane.",
        Icon = new(ImageMoniker.KnownValues.BulletList, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunInWorkspacesAsync("List Lint Rules", ["config", "ls-lint-rules"]);
        return Task.CompletedTask;
    }
}
