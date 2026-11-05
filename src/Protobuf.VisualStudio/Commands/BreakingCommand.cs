using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class BreakingCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Check Breaking Changes")
    {
        TooltipText = "Run 'buf breaking' in each Buf workspace against the git default branch (or Tools > Options > Buf > Breaking change baseline) and list the breaking changes in the Error List.",
        Icon = new(ImageMoniker.KnownValues.CompareFiles, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        _ = service.RunCheckAsync(new BufCheck(BufCheckKind.Breaking, "Check Breaking Changes", this.ArgumentsAsync));
        return Task.CompletedTask;
    }

    private async Task<IReadOnlyList<string>?> ArgumentsAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var settings = await BufSettings.ReadAsync(this.Extensibility, cancellationToken);
        var baseline = await BreakingBaseline.ResolveAsync(workspaceRoot, settings.BreakingAgainst, BreakingBaseline.RunGitAsync, cancellationToken);
        if (baseline.Against is null)
        {
            await service.LogAsync(baseline.Error!);
            return null;
        }

        return ["breaking", "--against", baseline.Against, "--error-format", "json"];
    }
}
