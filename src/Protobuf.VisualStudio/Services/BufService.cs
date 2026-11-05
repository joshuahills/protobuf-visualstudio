using System.Diagnostics;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Documents;
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

    public BufService(VisualStudioExtensibility extensibility, TraceSource? trace, CancellationToken shutdownToken)
    {
        this.extensibility = extensibility;
        this.trace = trace;
        this.shutdownToken = shutdownToken;
    }

    /// <summary>Raised when the language server should be restarted (settings changed, buf installed, user command).</summary>
    public event EventHandler? RestartRequested;

    public CancellationToken ShutdownToken => this.shutdownToken;

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
    public async Task RunInWorkspacesAsync(string title, IReadOnlyList<string> arguments, bool perWorkspace = true)
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

            var roots = perWorkspace ? WorkspaceRoots.Find(solutionDirectory) : [solutionDirectory];
            using var progress = await this.extensibility.Shell()
                .StartProgressReportingAsync($"Buf: {title}", new ProgressReporterOptions(isWorkCancellable: true), this.shutdownToken)
                .ConfigureAwait(false);

            var failures = 0;
            foreach (var root in roots)
            {
                progress.Report(new ProgressStatus(null, $"{BufCli.FormatCommandLine(arguments)} in {root}"));
                await this.LogAsync($"> {BufCli.FormatCommandLine(arguments)}   (in {root})").ConfigureAwait(false);
                var stopwatch = Stopwatch.StartNew();
                var result = await BufCli.RunAsync(buf.Path, arguments, root, progress.CancellationToken).ConfigureAwait(false);
                await this.LogBlockAsync(result.StandardOutput).ConfigureAwait(false);
                await this.LogBlockAsync(result.StandardError).ConfigureAwait(false);
                await this.LogAsync($"Exit code {result.ExitCode} after {stopwatch.Elapsed.TotalSeconds:F1}s.").ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    failures++;
                }
            }

            var summary = failures == 0
                ? $"{title}: finished in {roots.Count} workspace(s)."
                : $"{title}: {failures} of {roots.Count} workspace(s) reported problems. See the Buf output pane.";
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
