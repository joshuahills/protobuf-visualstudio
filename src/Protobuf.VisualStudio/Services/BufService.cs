using System.Diagnostics;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Documents;
using Microsoft.VisualStudio.Extensibility.Languages;
using Microsoft.VisualStudio.RpcContracts.Documents;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.RpcContracts.OutputChannel;
using Microsoft.VisualStudio.RpcContracts.ProgressReporting;
using Microsoft.VisualStudio.ProjectSystem.Query;

namespace Protobuf.VisualStudio.Services;

/// <summary>
/// Shared state for the extension: resolving (and if needed installing) buf.exe, running buf commands against the
/// Buf workspaces in the open solution, and the "Buf" output pane.
/// </summary>
internal sealed class BufService : IDisposable
{
    private readonly VisualStudioExtensibility extensibility;
    private readonly TraceSource? trace;
    private readonly CancellationToken shutdownToken;
    private readonly SemaphoreSlim resolveGate = new(1, 1);
    private readonly SemaphoreSlim outputGate = new(1, 1);
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private OutputChannel? output;
    private bool installDeclined;
    private BufDiagnosticsPublisher? diagnostics;
    private IDisposable? documentSubscription;

    public BufService(VisualStudioExtensibility extensibility, TraceSource? trace, CancellationToken shutdownToken)
    {
        this.extensibility = extensibility;
        this.trace = trace;
        this.shutdownToken = shutdownToken;
    }

    /// <summary>Raised when the language server should be restarted (settings changed, buf installed, user command).</summary>
    public event EventHandler? RestartRequested;

    public CancellationToken ShutdownToken => this.shutdownToken;

    private BufDiagnosticsPublisher Diagnostics => this.diagnostics ??= new BufDiagnosticsPublisher(this.extensibility);

    public VisualStudioExtensibility Extensibility => this.extensibility;

    public void RequestLanguageServerRestart() => this.RestartRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The folder of the open solution or folder, or null when nothing is open.</summary>
    public async Task<string?> GetSolutionDirectoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var solutions = await this.extensibility.Workspaces()
                .QuerySolutionAsync(solution => solution.With(s => new { s.Path, s.Directory }), cancellationToken)
                .ConfigureAwait(false);
            var solution = solutions.FirstOrDefault();
            var directory = solution?.Directory;
            if (string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(solution?.Path))
            {
                directory = Path.GetDirectoryName(solution.Path);
            }

            return string.IsNullOrEmpty(directory) ? null : directory;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.trace?.TraceEvent(TraceEventType.Warning, 0, $"Solution query failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds buf.exe and its version. When none is found and auto-install is on, downloads the latest release once
    /// per session.
    /// </summary>
    public async Task<BufBinary?> ResolveBufAsync(bool allowInstall, CancellationToken cancellationToken)
    {
        await this.resolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await BufSettings.ReadAsync(this.extensibility, cancellationToken).ConfigureAwait(false);
            var baseDirectory = await this.GetSolutionDirectoryAsync(cancellationToken).ConfigureAwait(false);
            var binary = await BufCli.ResolveAsync(settings.BufPath, baseDirectory, cancellationToken).ConfigureAwait(false);
            if (binary is not null)
            {
                return binary;
            }

            if (!string.IsNullOrEmpty(settings.BufPath))
            {
                await this.LogAsync($"The configured Buf CLI path '{settings.BufPath}' does not point to a working buf.exe.").ConfigureAwait(false);
                return null;
            }

            if (!allowInstall || !settings.AutoInstall || this.installDeclined)
            {
                await this.LogAsync("No buf.exe found on PATH or in %LOCALAPPDATA%\\BufForVisualStudio. Use Tools > Buf > Install or Update Buf CLI, or set Tools > Options > Buf > Buf CLI path.").ConfigureAwait(false);
                return null;
            }

            await this.LogAsync("No buf.exe found; downloading the latest release because 'Download the Buf CLI if it is missing' is on.").ConfigureAwait(false);
            try
            {
                await this.InstallCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Do not retry on every document open in this session.
                this.installDeclined = true;
                await this.LogAsync($"Buf CLI download failed: {ex.Message}").ConfigureAwait(false);
                return null;
            }

            return await BufCli.ResolveAsync(null, baseDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.resolveGate.Release();
        }
    }

    /// <summary>Installs (or updates to) the latest release, then restarts the language server so it picks it up.</summary>
    public async Task InstallOrUpdateAsync()
    {
        try
        {
            var exe = await this.InstallCoreAsync(this.shutdownToken).ConfigureAwait(false);
            this.installDeclined = false;
            var settings = await BufSettings.ReadAsync(this.extensibility, this.shutdownToken).ConfigureAwait(false);
            var active = BufCli.Locate(settings.BufPath, await this.GetSolutionDirectoryAsync(this.shutdownToken).ConfigureAwait(false));
            var note = active is not null && !string.Equals(active, exe, StringComparison.OrdinalIgnoreCase)
                ? $"\n\nNote: {active} takes precedence (configured path or PATH), so that copy stays in use."
                : string.Empty;
            this.RequestLanguageServerRestart();
            await this.extensibility.Shell().ShowPromptAsync($"Buf CLI installed at {exe}.{note}", PromptOptions.OK, this.shutdownToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await this.LogAsync("Buf CLI download cancelled.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await this.LogAsync($"Buf CLI download failed: {ex}").ConfigureAwait(false);
            await this.extensibility.Shell().ShowPromptAsync($"Buf CLI download failed: {ex.Message}", PromptOptions.ErrorConfirm, this.shutdownToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs <c>buf {arguments}</c> in each Buf workspace of the open solution and writes the output to the Buf pane.
    /// </summary>
    /// <param name="title">Shown in the progress indicator and the log.</param>
    /// <param name="perWorkspace">False to run once in the solution folder instead of once per workspace.</param>
    public Task RunInWorkspacesAsync(string title, IReadOnlyList<string> arguments, bool perWorkspace = true) =>
        this.RunCoreAsync(title, perWorkspace, (_, _) => Task.FromResult<IReadOnlyList<string>?>(arguments), check: null);

    /// <summary>
    /// Runs a check (build, lint, breaking) in each Buf workspace and replaces that check's Error List results with
    /// what it found. Findings are also written to the Buf pane as clickable lines.
    /// </summary>
    public Task RunCheckAsync(BufCheck check) => this.RunCoreAsync(check.Title, perWorkspace: true, check.Arguments, check);

    /// <summary>Removes every Tools &gt; Buf result from the Error List.</summary>
    public async Task ClearResultsAsync()
    {
        await this.Diagnostics.ClearAllAsync(this.shutdownToken).ConfigureAwait(false);
        await this.LogAsync("Cleared Buf results from the Error List.").ConfigureAwait(false);
    }

    private async Task RunCoreAsync(string title, bool perWorkspace, Func<string, CancellationToken, Task<IReadOnlyList<string>?>> argumentsFor, BufCheck? check)
    {
        if (!await this.commandGate.WaitAsync(0, this.shutdownToken).ConfigureAwait(false))
        {
            await this.extensibility.Shell().ShowPromptAsync("Another buf command is still running; see the Buf output pane.", PromptOptions.OK, this.shutdownToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var solutionDirectory = await this.GetSolutionDirectoryAsync(this.shutdownToken).ConfigureAwait(false);
            if (solutionDirectory is null)
            {
                await this.extensibility.Shell().ShowPromptAsync("Open a solution or folder first.", PromptOptions.OK, this.shutdownToken).ConfigureAwait(false);
                return;
            }

            var buf = await this.ResolveBufAsync(allowInstall: true, this.shutdownToken).ConfigureAwait(false);
            if (buf is null)
            {
                await this.extensibility.Shell().ShowPromptAsync("The Buf CLI was not found. Use Tools > Buf > Install or Update Buf CLI, or set its path in Tools > Options > Buf.", PromptOptions.OK, this.shutdownToken).ConfigureAwait(false);
                return;
            }

            if (check is not null)
            {
                await this.EnsureSaveTrackingAsync().ConfigureAwait(false);
            }

            var roots = perWorkspace ? WorkspaceRoots.Find(solutionDirectory) : [solutionDirectory];
            using var progress = await this.extensibility.Shell()
                .StartProgressReportingAsync($"Buf: {title}", new ProgressReporterOptions(isWorkCancellable: true), this.shutdownToken)
                .ConfigureAwait(false);

            var failures = 0;
            List<BufAnnotation> findings = [];
            foreach (var root in roots)
            {
                var arguments = await argumentsFor(root, progress.CancellationToken).ConfigureAwait(false);
                if (arguments is null)
                {
                    failures++;
                    continue;
                }

                progress.Report(new ProgressStatus(null, $"{BufCli.FormatCommandLine(arguments)} in {root}"));
                await this.LogAsync($"> {BufCli.FormatCommandLine(arguments)}   (in {root})").ConfigureAwait(false);
                var stopwatch = Stopwatch.StartNew();
                var result = await BufCli.RunAsync(buf.Path, arguments, root, progress.CancellationToken).ConfigureAwait(false);

                if (check is null)
                {
                    await this.LogBlockAsync(result.StandardOutput).ConfigureAwait(false);
                    await this.LogBlockAsync(result.StandardError).ConfigureAwait(false);
                }
                else
                {
                    var annotations = BufAnnotation.ParseOutput(result, root);
                    findings.AddRange(annotations);
                    foreach (var annotation in annotations)
                    {
                        var severity = BufDiagnosticMapper.Severity(annotation, check.Kind) == BufSeverity.Error ? "error" : "warning";
                        await this.WriteAsync(annotation.ToMsBuildLine(severity)).ConfigureAwait(false);
                    }

                    await this.LogBlockAsync(BufAnnotation.NonJsonLines(result.StandardError)).ConfigureAwait(false);
                }

                await this.LogAsync($"Exit code {result.ExitCode} after {stopwatch.Elapsed.TotalSeconds:F1}s.").ConfigureAwait(false);
                var ranCleanly = result.ExitCode == 0 || (check is not null && result.ExitCode == BufCheck.FindingsExitCode);
                if (!ranCleanly)
                {
                    failures++;
                }
            }

            if (check is not null)
            {
                await this.Diagnostics.PublishAsync(check.Kind, findings, this.shutdownToken).ConfigureAwait(false);
            }

            var summary = (failures, check) switch
            {
                (0, null) => $"{title}: finished in {roots.Count} workspace(s).",
                (0, _) when findings.Count == 0 => $"{title}: no problems in {roots.Count} workspace(s).",
                (0, _) => $"{title}: {findings.Count} problem(s) in {roots.Count} workspace(s); see the Error List.",
                _ => $"{title}: buf failed in {failures} of {roots.Count} workspace(s). See the Buf output pane.",
            };
            await this.LogAsync(summary).ConfigureAwait(false);
            if (failures > 0)
            {
                await this.extensibility.Shell().ShowPromptAsync(summary, PromptOptions.OK, this.shutdownToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await this.LogAsync($"{title}: cancelled.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await this.LogAsync($"{title} failed: {ex}").ConfigureAwait(false);
        }
        finally
        {
            this.commandGate.Release();
        }
    }

    /// <summary>
    /// Starts listening for document saves (once), so a saved file's check results are dropped from the Error List;
    /// the language server's live diagnostics cover it from then on.
    /// </summary>
    private async Task EnsureSaveTrackingAsync()
    {
        if (this.documentSubscription is not null)
        {
            return;
        }

        var subscription = await this.extensibility.Documents().SubscribeAsync(new SaveListener(this), filterRegex: null, this.shutdownToken).ConfigureAwait(false);
        if (Interlocked.CompareExchange(ref this.documentSubscription, subscription, null) is not null)
        {
            subscription.Dispose();
        }
    }

    private async Task OnDocumentSavedAsync(Uri moniker)
    {
        if (!moniker.IsFile || !moniker.LocalPath.EndsWith(".proto", StringComparison.OrdinalIgnoreCase) || this.diagnostics is null)
        {
            return;
        }

        try
        {
            await this.diagnostics.ClearFileAsync(moniker.LocalPath, this.shutdownToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.trace?.TraceEvent(TraceEventType.Warning, 0, $"Clearing results for {moniker} failed: {ex.Message}");
        }
    }

    private sealed class SaveListener(BufService owner) : IDocumentEventsListener
    {
        public Task SavedAsync(DocumentEventArgs e, CancellationToken token)
        {
            _ = owner.OnDocumentSavedAsync(e.Moniker);
            return Task.CompletedTask;
        }

        public Task OpenedAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;

        public Task ClosedAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;

        public Task SavingAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;

        public Task RenamedAsync(RenamedDocumentEventArgs e, CancellationToken token) => Task.CompletedTask;

        public Task ShownAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;

        public Task HiddenAsync(DocumentEventArgs e, CancellationToken token) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    public Task LogAsync(string line) => this.WriteAsync($"[{DateTime.Now:HH:mm:ss}] {line}");

    /// <summary>Writes multi-line tool output verbatim (no timestamp), skipping empty text.</summary>
    public async Task LogBlockAsync(string text)
    {
        text = text.TrimEnd();
        if (text.Length > 0)
        {
            await this.WriteAsync(text).ConfigureAwait(false);
        }
    }

    public void Log(string line) => _ = this.LogAsync(line);

    public void Dispose()
    {
        this.output?.Dispose();
        this.documentSubscription?.Dispose();
        this.diagnostics?.Dispose();
        this.resolveGate.Dispose();
        this.outputGate.Dispose();
        this.commandGate.Dispose();
    }

    private async Task<string> InstallCoreAsync(CancellationToken cancellationToken)
    {
        using var progress = await this.extensibility.Shell()
            .StartProgressReportingAsync("Installing Buf CLI", new ProgressReporterOptions(isWorkCancellable: true), cancellationToken)
            .ConfigureAwait(false);
        var reporter = new Progress<double>(fraction => progress.Report(new ProgressStatus((int)(fraction * 100), $"Downloading Buf CLI {fraction:P0}")));
        return await BufInstaller.InstallAsync(BufCli.DefaultInstallRoot, this.Log, reporter, progress.CancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync(string text)
    {
        this.trace?.TraceInformation(text);
        try
        {
            await this.outputGate.WaitAsync(this.shutdownToken).ConfigureAwait(false);
            try
            {
                this.output ??= await this.extensibility.Views().Output.CreateOutputChannelAsync("Buf", this.shutdownToken).ConfigureAwait(false);
                await this.output.WriteLineAsync(text).ConfigureAwait(false);
            }
            finally
            {
                this.outputGate.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.trace?.TraceEvent(TraceEventType.Warning, 0, $"Output pane write failed: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
        }
    }
}
