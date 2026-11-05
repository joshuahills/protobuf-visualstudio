using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class ConfigInitCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Initialize buf.yaml")
    {
        TooltipText = "Run 'buf config init' in the solution folder to create a buf.yaml.",
        Icon = new(ImageMoniker.KnownValues.NewDocument, IconSettings.IconAndText),
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var directory = await service.GetSolutionDirectoryAsync(cancellationToken);
        if (directory is not null && File.Exists(Path.Combine(directory, "buf.yaml")))
        {
            await this.Extensibility.Shell().ShowPromptAsync($"{Path.Combine(directory, "buf.yaml")} already exists.", PromptOptions.OK, cancellationToken);
            return;
        }

        await service.RunInWorkspacesAsync("Initialize buf.yaml", ["config", "init"], perWorkspace: false);
    }
}
