using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.Commands;

[VisualStudioContribution]
internal sealed class InstallBufCommand(BufService service) : Command
{
    public override CommandConfiguration CommandConfiguration => new("Install or Update Buf CLI...")
    {
        TooltipText = "Download the latest Buf CLI release from GitHub into %LOCALAPPDATA%\\BufForVisualStudio.",
        Icon = new(ImageMoniker.KnownValues.Download, IconSettings.IconAndText),
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var current = await service.ResolveBufAsync(allowInstall: false, cancellationToken);
        var message = current is null
            ? "Download the latest Buf CLI release (about 50 MB) from github.com/bufbuild/buf into %LOCALAPPDATA%\\BufForVisualStudio?"
            : $"Buf {current.Version} is in use ({current.Path}).\n\nDownload the latest release into %LOCALAPPDATA%\\BufForVisualStudio?";

        if (await this.Extensibility.Shell().ShowPromptAsync(message, PromptOptions.OKCancel, cancellationToken))
        {
            _ = service.InstallOrUpdateAsync();
        }
    }
}
