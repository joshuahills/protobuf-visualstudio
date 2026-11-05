using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Protobuf.VisualStudio.Services;

/// <summary>A resolved buf.exe and its version.</summary>
internal sealed record BufBinary(string Path, Version Version)
{
    /// <summary>First release with <c>buf beta lsp</c>.</summary>
    public static readonly Version MinimumBetaLspVersion = new(1, 43, 0);

    /// <summary>First release with the stable <c>buf lsp serve</c>.</summary>
    public static readonly Version MinimumLspVersion = new(1, 59, 0);

    public bool SupportsLanguageServer => this.Version >= MinimumBetaLspVersion;

    /// <summary>Arguments that start the language server on stdio, matching what the Buf VS Code extension passes.</summary>
    public IReadOnlyList<string> LanguageServerArguments(bool debug)
    {
        List<string> args = ["--log-format", "text"];
        if (debug)
        {
            args.Add("--debug");
        }

        args.AddRange(this.Version >= MinimumLspVersion ? ["lsp", "serve"] : ["beta", "lsp"]);
        return args;
    }
}

/// <summary>The output of a finished buf invocation.</summary>
internal sealed record BufResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Finds buf.exe and runs it.</summary>
internal static partial class BufCli
{
    public static string DefaultInstallRoot { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BufForVisualStudio", "buf");

    /// <summary>
    /// Resolves buf.exe in order: the configured path (file or folder, relative to <paramref name="baseDirectory"/>),
    /// PATH, then the newest copy under <see cref="DefaultInstallRoot"/>.
    /// </summary>
    public static string? Locate(string? configuredPath, string? baseDirectory) =>
        Locate(configuredPath, baseDirectory, Environment.GetEnvironmentVariable("PATH"), DefaultInstallRoot);

    /// <summary>Test seam: the PATH value and install root are passed in instead of read from the machine.</summary>
    internal static string? Locate(string? configuredPath, string? baseDirectory, string? pathVariable, string installRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
            if (!System.IO.Path.IsPathRooted(expanded) && baseDirectory is not null)
            {
                expanded = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, expanded));
            }

            if (Directory.Exists(expanded))
            {
                expanded = System.IO.Path.Combine(expanded, "buf.exe");
            }

            return File.Exists(expanded) ? expanded : null;
        }

        foreach (var dir in (pathVariable ?? string.Empty).Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = System.IO.Path.Combine(dir.Trim().Trim('"'), "buf.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }

        return FindInstalled(installRoot);
    }

    /// <summary>Returns the highest-versioned <c>v*\buf.exe</c> under <paramref name="installRoot"/>.</summary>
    public static string? FindInstalled(string installRoot)
    {
        if (!Directory.Exists(installRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(installRoot, "v*")
            .Select(dir => (dir, version: ParseVersion(System.IO.Path.GetFileName(dir))))
            .Where(x => x.version is not null && File.Exists(System.IO.Path.Combine(x.dir, "buf.exe")))
            .OrderByDescending(x => x.version)
            .Select(x => System.IO.Path.Combine(x.dir, "buf.exe"))
            .FirstOrDefault();
    }

    public static async Task<BufBinary?> ResolveAsync(string? configuredPath, string? baseDirectory, CancellationToken cancellationToken)
    {
        var path = Locate(configuredPath, baseDirectory);
        if (path is null)
        {
            return null;
        }

        var result = await RunAsync(path, ["--version"], baseDirectory, cancellationToken).ConfigureAwait(false);

        // Older releases print the version on stderr.
        var version = ParseVersion(result.StandardOutput) ?? ParseVersion(result.StandardError);
        return version is null ? null : new BufBinary(path, version);
    }

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = VersionPattern().Match(text);
        return match.Success ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)) : null;
    }

    /// <summary>Runs buf to completion and captures its output.</summary>
    public static async Task<BufResult> RunAsync(string bufPath, IEnumerable<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var info = CreateStartInfo(bufPath, arguments, workingDirectory);
        info.RedirectStandardInput = false;

        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new BufResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    public static ProcessStartInfo CreateStartInfo(string bufPath, IEnumerable<string> arguments, string? workingDirectory)
    {
        var info = new ProcessStartInfo(bufPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
        {
            info.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    public static string FormatCommandLine(IEnumerable<string> arguments) =>
        "buf " + string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    [GeneratedRegex(@"v?(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionPattern();
}
