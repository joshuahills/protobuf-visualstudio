using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace Protobuf.VisualStudio;

/// <summary>Values read from Tools &gt; Options &gt; Buf when they are needed.</summary>
internal sealed record EffectiveSettings(string BufPath, bool AutoInstall, bool DebugLogs, string BreakingAgainst);

/// <summary>Settings surfaced under Tools &gt; Options &gt; Buf.</summary>
internal static class BufSettings
{
    [VisualStudioContribution]
    public static SettingCategory Category { get; } = new("buf", "Buf")
    {
        Description = "Protocol Buffers support powered by the Buf CLI and language server.",
    };

    [VisualStudioContribution]
    public static Setting.String BufPath { get; } = new("bufPath", "Buf CLI path", Category, string.Empty)
    {
        Description = "Path to buf.exe or the folder that contains it. Relative paths resolve against the solution folder. Leave empty to use buf from PATH, or the copy this extension installed.",
    };

    [VisualStudioContribution]
    public static Setting.Boolean AutoInstall { get; } = new("autoInstall", "Download the Buf CLI if it is missing", Category, defaultValue: true)
    {
        Description = "When no buf.exe is found, download the latest release from github.com/bufbuild/buf (checksum verified) into %LOCALAPPDATA%\\BufForVisualStudio.",
    };

    [VisualStudioContribution]
    public static Setting.Boolean DebugLogs { get; } = new("debugLogs", "Language server debug logging", Category, defaultValue: false)
    {
        Description = "Start the language server with --debug and write its log to the 'Buf' output pane. Takes effect when the server restarts.",
    };

    [VisualStudioContribution]
    public static Setting.String BreakingAgainst { get; } = new("breakingAgainst", "Breaking change baseline", Category, string.Empty)
    {
        Description = "What Check Breaking Changes compares against: any buf input, such as .git#branch=develop, .git#tag=v1.0.0 or buf.build/owner/module. Leave empty to use the git default branch (origin/HEAD, then main, then master) of the repository containing each workspace.",
    };

    public static async Task<EffectiveSettings> ReadAsync(VisualStudioExtensibility extensibility, CancellationToken cancellationToken)
    {
        SettingCategory[] categories = [Category];
        var values = await extensibility.Settings().ReadEffectiveValuesAsync(categories, cancellationToken).ConfigureAwait(false);
        return new EffectiveSettings(
            BufPath: values.ValueOrDefault(BufPath, string.Empty).Trim(),
            AutoInstall: values.ValueOrDefault(AutoInstall, true),
            DebugLogs: values.ValueOrDefault(DebugLogs, false),
            BreakingAgainst: values.ValueOrDefault(BreakingAgainst, string.Empty).Trim());
    }
}
