using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Documents;
using Microsoft.VisualStudio.Extensibility.Languages;
using Microsoft.VisualStudio.RpcContracts.DiagnosticManagement;
using Range = Microsoft.VisualStudio.RpcContracts.Utilities.Range;

namespace Protobuf.VisualStudio.Services;

/// <summary>
/// Publishes findings from Tools &gt; Buf checks to the Error List, one diagnostics provider per check kind so a
/// new lint run replaces old lint results without touching build or breaking results.
/// </summary>
/// <remarks>
/// The language server already reports live diagnostics for open files. These are a snapshot from the last run, so
/// a file's entries are dropped as soon as it is saved, and the live ones take over.
/// </remarks>
internal sealed class BufDiagnosticsPublisher(VisualStudioExtensibility extensibility) : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<BufCheckKind, DiagnosticsReporter> reporters = [];
    private readonly Dictionary<BufCheckKind, HashSet<string>> files = [];

    /// <summary>Replaces the results of <paramref name="kind"/> with <paramref name="annotations"/>.</summary>
    public async Task PublishAsync(BufCheckKind kind, IReadOnlyCollection<BufAnnotation> annotations, CancellationToken cancellationToken)
    {
        await this.ClearKindAsync(kind, cancellationToken).ConfigureAwait(false);
        if (annotations.Count == 0)
        {
            return;
        }

        var diagnostics = annotations.Select(a => ToVisualStudio(BufDiagnosticMapper.ToDiagnostic(a, kind))).ToList();
        lock (this.gate)
        {
            this.TrackedFiles(kind).UnionWith(annotations.Select(a => a.Path));
        }

        await this.Reporter(kind).ReportDiagnosticsAsync(diagnostics, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops every kind's results for one file (called when the file is saved).</summary>
    public async Task ClearFileAsync(string path, CancellationToken cancellationToken)
    {
        List<BufCheckKind> kinds;
        lock (this.gate)
        {
            kinds = this.files.Where(pair => pair.Value.Remove(path)).Select(pair => pair.Key).ToList();
        }

        foreach (var kind in kinds)
        {
            await this.Reporter(kind).ClearDiagnosticsAsync(new Uri(path), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        foreach (var kind in Enum.GetValues<BufCheckKind>())
        {
            await this.ClearKindAsync(kind, cancellationToken).ConfigureAwait(false);
        }
    }

    public bool HasResults
    {
        get
        {
            lock (this.gate)
            {
                return this.files.Values.Any(set => set.Count > 0);
            }
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            foreach (var reporter in this.reporters.Values)
            {
                reporter.Dispose();
            }

            this.reporters.Clear();
        }
    }

    private async Task ClearKindAsync(BufCheckKind kind, CancellationToken cancellationToken)
    {
        string[] previous;
        lock (this.gate)
        {
            var set = this.TrackedFiles(kind);
            previous = [.. set];
            set.Clear();
        }

        foreach (var path in previous)
        {
            await this.Reporter(kind).ClearDiagnosticsAsync(new Uri(path), cancellationToken).ConfigureAwait(false);
        }
    }

    private static DocumentDiagnostic ToVisualStudio(BufDiagnostic d) =>
        new(new Uri(d.Path), new Range(d.StartLine, d.StartColumn, d.EndLine, d.EndColumn), d.Message)
        {
            ErrorCode = d.Code,
            Severity = d.Severity == BufSeverity.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            ProviderName = d.ProviderName,
            HelpLink = d.HelpLink,
        };

    private HashSet<string> TrackedFiles(BufCheckKind kind)
    {
        if (!this.files.TryGetValue(kind, out var set))
        {
            this.files[kind] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return set;
    }

    private DiagnosticsReporter Reporter(BufCheckKind kind)
    {
        lock (this.gate)
        {
            if (!this.reporters.TryGetValue(kind, out var reporter))
            {
                this.reporters[kind] = reporter = extensibility.Languages().GetDiagnosticsReporter(BufDiagnosticMapper.ProviderName(kind));
            }

            return reporter;
        }
    }
}
