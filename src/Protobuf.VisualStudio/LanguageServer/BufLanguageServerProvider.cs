using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;
using Nerdbank.Streams;
using Protobuf.VisualStudio.Services;

namespace Protobuf.VisualStudio.LanguageServer;

/// <summary>
/// Connects Visual Studio's LSP client to <c>buf lsp serve</c> on stdio. Everything the server advertises
/// (completion, hover, go to definition, find references, rename, formatting, semantic colouring, document symbols,
/// folding, document links, code actions and build/lint diagnostics) is wired up by Visual Studio from the server's
/// capabilities; this class only finds buf and starts the process.
/// </summary>
[VisualStudioContribution]
internal sealed class BufLanguageServerProvider : LanguageServerProvider
{
    private readonly BufService service;
    private readonly object processGate = new();
    private Process? process;

    public BufLanguageServerProvider(BufService service)
    {
        this.service = service;
        this.service.RestartRequested += (_, _) => _ = this.RestartAsync();
    }

    /// <summary>Protocol Buffers source files.</summary>
    [VisualStudioContribution]
    public static DocumentTypeConfiguration ProtobufDocumentType => new("protobuf")
    {
        FileExtensions = [".proto"],
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new(
        "%Protobuf.VisualStudio.BufLanguageServer.DisplayName%",
        [DocumentFilter.FromDocumentType(ProtobufDocumentType)]);

    public override async Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        var buf = await this.service.ResolveBufAsync(allowInstall: true, cancellationToken).ConfigureAwait(false);
        if (buf is null)
        {
            return null;
        }

        if (!buf.SupportsLanguageServer)
        {
            await this.service.LogAsync($"Buf {buf.Version} at {buf.Path} is older than {BufBinary.MinimumBetaLspVersion}, the first release with a language server. Update it with Tools > Buf > Install or Update Buf CLI.").ConfigureAwait(false);
            return null;
        }

        var settings = await BufSettings.ReadAsync(this.Extensibility, cancellationToken).ConfigureAwait(false);
        var arguments = buf.LanguageServerArguments(settings.DebugLogs);
        var workingDirectory = await this.service.GetSolutionDirectoryAsync(cancellationToken).ConfigureAwait(false);

        var started = new Process { StartInfo = BufCli.CreateStartInfo(buf.Path, arguments, workingDirectory), EnableRaisingEvents = true };
        if (!started.Start())
        {
            started.Dispose();
            await this.service.LogAsync($"Could not start {buf.Path}.").ConfigureAwait(false);
            return null;
        }

        lock (this.processGate)
        {
            this.process = started;
        }

        await this.service.LogAsync($"Started Buf {buf.Version} language server: {BufCli.FormatCommandLine(arguments)} (pid {started.Id}, {buf.Path})").ConfigureAwait(false);
        started.Exited += (_, _) => this.service.Log($"Buf language server (pid {started.Id}) exited with code {SafeExitCode(started)}.");
        _ = this.PumpStandardErrorAsync(started);

        return new DuplexPipe(
            PipeReader.Create(started.StandardOutput.BaseStream),
            PipeWriter.Create(started.StandardInput.BaseStream));
    }

    public override async Task OnServerInitializationResultAsync(ServerInitializationResult serverInitializationResult, LanguageServerInitializationFailureInfo? initializationFailureInfo, CancellationToken cancellationToken)
    {
        if (serverInitializationResult == ServerInitializationResult.Failed)
        {
            await this.service.LogAsync($"Buf language server failed to initialize: {initializationFailureInfo?.StatusMessage} {initializationFailureInfo?.Exception}").ConfigureAwait(false);

            // Do not keep relaunching a server that cannot start; Restart Language Server re-enables it.
            this.Enabled = false;
        }
        else
        {
            await this.service.LogAsync("Buf language server initialized.").ConfigureAwait(false);
        }

        await base.OnServerInitializationResultAsync(serverInitializationResult, initializationFailureInfo, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.KillProcess();
        }

        base.Dispose(disposing);
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            return "unknown";
        }
    }

    private async Task RestartAsync()
    {
        try
        {
            await this.service.LogAsync("Restarting the Buf language server.").ConfigureAwait(false);

            // Tell Visual Studio to drop the client, end the process, then let it activate again.
            this.Enabled = false;
            this.KillProcess();
            await Task.Delay(500, this.service.ShutdownToken).ConfigureAwait(false);
            this.Enabled = true;
        }
        catch (Exception ex)
        {
            await this.service.LogAsync($"Language server restart failed: {ex.Message}").ConfigureAwait(false);
        }
    }

    private void KillProcess()
    {
        Process? running;
        lock (this.processGate)
        {
            running = this.process;
            this.process = null;
        }

        if (running is not null)
        {
            BufCli.TryKill(running);
            running.Dispose();
        }
    }

    /// <summary>buf writes its log to stderr; drain it so the pipe never fills, and surface it in the Buf pane.</summary>
    private async Task PumpStandardErrorAsync(Process running)
    {
        try
        {
            string? line;
            while ((line = await running.StandardError.ReadLineAsync(this.service.ShutdownToken).ConfigureAwait(false)) is not null)
            {
                await this.service.LogBlockAsync("  [lsp] " + line).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException or ObjectDisposedException)
        {
        }
    }
}
