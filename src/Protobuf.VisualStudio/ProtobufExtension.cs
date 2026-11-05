using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Protobuf.VisualStudio.Commands;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio;

/// <summary>Extension entry point: metadata, the Tools &gt; Buf menu, and the shared <see cref="BufService"/>.</summary>
[VisualStudioContribution]
internal sealed class ProtobufExtension : Extension
{
    private readonly object gate = new();
    private BufService? service;

    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "Protobuf.VisualStudio.4f6c2a9e-8d1b-4e57-9c3a-2b7e6f0d5a14",
            version: this.ExtensionAssemblyVersion,
            publisherName: "Josh Hills",
            displayName: "Protobuf Language Support",
            description: "Protocol Buffers IntelliSense for Visual Studio, powered by the Buf language server: completion, go to definition, hover, references, rename, formatting, and build and lint diagnostics as you type.")
        {
            Tags = ["protobuf", "proto", "protocol buffers", "buf", "grpc", "lsp"],
            MoreInfo = "https://buf.build/docs/",
            Icon = @"Resources\Icon.png",
            // Built for .NET 10, which needs the .NET 10 extension host that ships with Visual Studio 2026.
            // The value must be the "netX.Y" form; VS 2026 ignores extensions declaring "10.0".
            DotnetTargetVersions = [DotnetTarget.Custom("net10.0")],
            // The Marketplace rejects an 18.0 lower bound ("experimental API version"; VsixPub0029), so declare
            // 17.14. In practice the extension only runs on VS 2026, because VS 2022 has no .NET 10 host.
            InstallationTargetVersion = "[17.14,)",
        },
    };

    [VisualStudioContribution]
    public static MenuConfiguration BufMenu => new("Buf")
    {
        Placements = [CommandPlacement.KnownPlacements.ToolsMenu],
        Children =
        [
            MenuChild.Command<BuildCommand>(),
            MenuChild.Command<LintCommand>(),
            MenuChild.Command<FormatCommand>(),
            MenuChild.Command<GenerateCommand>(),
            MenuChild.Command<ClearResultsCommand>(),
            MenuChild.Separator,
            MenuChild.Command<DepUpdateCommand>(),
            MenuChild.Command<DepPruneCommand>(),
            MenuChild.Command<ConfigInitCommand>(),
            MenuChild.Separator,
            MenuChild.Command<ListLintRulesCommand>(),
            MenuChild.Command<ListBreakingRulesCommand>(),
            MenuChild.Separator,
            MenuChild.Command<RestartLanguageServerCommand>(),
            MenuChild.Command<InstallBufCommand>(),
        ],
    };

    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
        serviceCollection.AddSingleton(provider => this.GetOrCreateService(provider.GetRequiredService<VisualStudioExtensibility>(), provider.GetService<TraceSource>()));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.service?.Dispose();
        }

        base.Dispose(disposing);
    }

    private BufService GetOrCreateService(VisualStudioExtensibility extensibility, TraceSource? traceSource)
    {
        lock (this.gate)
        {
            return this.service ??= new BufService(extensibility, traceSource, this.ShutdownToken);
        }
    }
}
