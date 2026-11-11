using GhostSlacking.Core;
using GhostSlacking.Platform;
namespace GhostSlacking.App.Tests;

// Guards stop these requests before any UI Automation/target API is invoked.
public sealed class BackgroundAutomationAsyncTests
{
    [Fact]
    public async Task An_issued_mutation_timeout_is_uncertain_and_is_never_replayed()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var automation = new BackgroundAutomationText();
        var calls = 0;
        var result = automation.NativeCommand(0, "SimulatedSelection", () => NativeResult.Ok("Guard"), 11, () =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(3));
            return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.EditCommand);
        });
        var completions = new List<BackgroundInputCompletion>();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (completions.Count == 0 && DateTimeOffset.UtcNow < deadline)
            {
                completions.AddRange(automation.DrainCompletions());
                if (completions.Count == 0) await Task.Delay(25);
            }
            var completion = Assert.Single(completions);
            Assert.Equal(result.PendingOperation, completion.Operation);
            Assert.False(completion.Dispatch.Result.Success);
            Assert.Equal("BackgroundAutomationOutcomeUnknown", completion.Dispatch.Result.Operation);
        }
        finally { release.Set(); }
        await Task.Delay(100);
        Assert.Equal(1, calls);
        Assert.Empty(automation.DrainCompletions());
    }

    [Fact]
    public async Task A_blocked_read_only_request_returns_pending_and_reset_discards_its_late_result()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var automation = new BackgroundAutomationText();
        var result = automation.Prepare(0, () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(3));
            return NativeResult.Failed("BackgroundInputIdentity", 0);
        }, 7);
        Assert.NotEqual(0, result.PendingOperation);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
            automation.Reset();
        }
        finally { release.Set(); }
        await Task.Delay(100);
        Assert.Empty(automation.DrainCompletions());
    }

    [Fact]
    public async Task A_read_only_timeout_has_one_soft_completion_without_waiting_for_the_provider_to_return()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var automation = new BackgroundAutomationText();
        var result = automation.Prepare(0, () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(4));
            return NativeResult.Failed("BackgroundInputIdentity", 0);
        }, 9);
        var completions = new List<BackgroundInputCompletion>();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (completions.Count == 0 && DateTimeOffset.UtcNow < deadline)
            {
                completions.AddRange(automation.DrainCompletions());
                if (completions.Count == 0) await Task.Delay(25);
            }
            var completion = Assert.Single(completions);
            Assert.Equal(result.PendingOperation, completion.Operation);
            Assert.Equal(9, completion.SessionGeneration);
            Assert.Equal(BackgroundInputRejectionReason.DiscoveryUnavailable, completion.Dispatch.Rejection);
        }
        finally { release.Set(); }
        await Task.Delay(100);
        Assert.Empty(automation.DrainCompletions());
    }
}
