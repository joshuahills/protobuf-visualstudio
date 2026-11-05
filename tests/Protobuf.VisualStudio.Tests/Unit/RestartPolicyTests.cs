using Protobuf.VisualStudio.LanguageServer;

namespace Protobuf.VisualStudio.Tests.Unit;

public class RestartPolicyTests
{
    private readonly ManualTime time = new();

    [Fact]
    public void The_initial_settings_never_cause_a_restart()
    {
        Assert.False(new RestartPolicy(this.time).SettingsChanged(new("", false)));
    }

    [Theory]
    [InlineData(@"C:\buf.exe", false, true)]
    [InlineData("", true, true)]
    [InlineData("", false, false)]
    public void Only_a_changed_launch_setting_restarts(string bufPath, bool debug, bool expected)
    {
        var policy = new RestartPolicy(this.time);
        policy.SettingsChanged(new("", false));

        Assert.Equal(expected, policy.SettingsChanged(new(bufPath, debug)));
    }

    [Fact]
    public void Changing_the_pinned_version_restarts()
    {
        var policy = new RestartPolicy(this.time);
        policy.SettingsChanged(new("", false, ""));

        Assert.True(policy.SettingsChanged(new("", false, "1.70.0")));
    }

    [Fact]
    public void Each_change_is_compared_with_the_previous_one()
    {
        var policy = new RestartPolicy(this.time);
        policy.SettingsChanged(new("", false));
        Assert.True(policy.SettingsChanged(new("", true)));
        Assert.False(policy.SettingsChanged(new("", true)));
        Assert.True(policy.SettingsChanged(new("", false)));
    }

    [Fact]
    public void Crashes_restart_up_to_the_budget_then_give_up()
    {
        var policy = new RestartPolicy(this.time);

        for (var i = 0; i < RestartPolicy.MaxCrashRestarts; i++)
        {
            policy.ServerStarted();
            Assert.Equal(RestartDecision.Restart, policy.ServerExited());
            this.time.Advance(TimeSpan.FromSeconds(10));
        }

        policy.ServerStarted();
        Assert.Equal(RestartDecision.GiveUp, policy.ServerExited());
    }

    [Fact]
    public void Old_crashes_fall_out_of_the_window()
    {
        var policy = new RestartPolicy(this.time);
        for (var i = 0; i < RestartPolicy.MaxCrashRestarts; i++)
        {
            policy.ServerExited();
        }

        this.time.Advance(RestartPolicy.CrashWindow + TimeSpan.FromSeconds(1));

        Assert.Equal(RestartDecision.Restart, policy.ServerExited());
    }

    [Fact]
    public void An_expected_exit_is_not_a_crash()
    {
        var policy = new RestartPolicy(this.time);
        for (var i = 0; i < RestartPolicy.MaxCrashRestarts; i++)
        {
            policy.ServerExited();
        }

        policy.ExpectExit();
        Assert.Equal(RestartDecision.Intentional, policy.ServerExited());

        // A new server clears the expectation, so its own crash counts again.
        policy.ServerStarted();
        Assert.Equal(RestartDecision.GiveUp, policy.ServerExited());
    }

    [Fact]
    public void Reset_restores_the_budget()
    {
        var policy = new RestartPolicy(this.time);
        for (var i = 0; i < RestartPolicy.MaxCrashRestarts; i++)
        {
            policy.ServerExited();
        }

        policy.Reset();

        Assert.Equal(RestartDecision.Restart, policy.ServerExited());
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.now;

        public void Advance(TimeSpan by) => this.now += by;
    }
}
