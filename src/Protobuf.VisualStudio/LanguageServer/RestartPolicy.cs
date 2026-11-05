namespace Protobuf.VisualStudio.LanguageServer;

/// <summary>The settings that change how the language server process is started.</summary>
internal sealed record ServerLaunchSettings(string BufPath, bool DebugLogs, string BufVersion = "");

/// <summary>
/// Decides when the Buf language server should be restarted automatically: when a launch setting changes, and after
/// an unexpected exit, up to <see cref="MaxCrashRestarts"/> times within <see cref="CrashWindow"/> (the same budget
/// the VS Code client uses). Restarts the extension asks for itself are not counted as crashes.
/// </summary>
internal sealed class RestartPolicy(TimeProvider time)
{
    public const int MaxCrashRestarts = 4;

    public static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(3);

    private readonly object gate = new();
    private readonly Queue<DateTimeOffset> crashes = new();
    private ServerLaunchSettings? lastSettings;
    private bool expectingExit;

    public RestartPolicy()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Records the latest launch settings and returns true when they differ from the previous ones. The first call
    /// (the initial values) never asks for a restart.
    /// </summary>
    public bool SettingsChanged(ServerLaunchSettings current)
    {
        lock (this.gate)
        {
            var previous = this.lastSettings;
            this.lastSettings = current;
            return previous is not null && previous != current;
        }
    }

    /// <summary>Call before the extension stops the server on purpose, so the exit isn't treated as a crash.</summary>
    public void ExpectExit()
    {
        lock (this.gate)
        {
            this.expectingExit = true;
        }
    }

    /// <summary>Call when a new server process has started.</summary>
    public void ServerStarted()
    {
        lock (this.gate)
        {
            this.expectingExit = false;
        }
    }

    /// <summary>Returns whether to restart after the server process exited.</summary>
    public RestartDecision ServerExited()
    {
        lock (this.gate)
        {
            if (this.expectingExit)
            {
                return RestartDecision.Intentional;
            }

            var now = time.GetUtcNow();
            while (this.crashes.Count > 0 && now - this.crashes.Peek() > CrashWindow)
            {
                this.crashes.Dequeue();
            }

            if (this.crashes.Count >= MaxCrashRestarts)
            {
                return RestartDecision.GiveUp;
            }

            this.crashes.Enqueue(now);
            return RestartDecision.Restart;
        }
    }

    /// <summary>Forgets earlier crashes, e.g. when the user restarts the server by hand.</summary>
    public void Reset()
    {
        lock (this.gate)
        {
            this.crashes.Clear();
        }
    }
}

internal enum RestartDecision
{
    /// <summary>The extension stopped the server itself; nothing to do.</summary>
    Intentional,

    /// <summary>Unexpected exit within budget: restart.</summary>
    Restart,

    /// <summary>Too many crashes in the window: leave it stopped until the user restarts it.</summary>
    GiveUp,
}
