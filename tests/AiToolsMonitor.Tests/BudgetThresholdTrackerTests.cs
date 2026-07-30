using AiToolsMonitor.Monitoring;

namespace AiToolsMonitor.Tests;

public class BudgetThresholdTrackerTests
{
    [Fact]
    public void GetNewlyExceededTools_ReturnsEachToolOnlyOncePerTracker()
    {
        var tracker = new BudgetThresholdTracker();

        var below = Snapshot(("Codex", 89.9), ("Claude Code", 10));
        var firstCrossing = Snapshot(("Codex", 90), ("Claude Code", 95));
        var laterPoll = Snapshot(("Codex", 99), ("Claude Code", 96));

        Assert.Empty(tracker.GetNewlyExceededTools(below));
        Assert.Equal(["Codex", "Claude Code"],
            tracker.GetNewlyExceededTools(firstCrossing).Select(tool => tool.DisplayName));
        Assert.Empty(tracker.GetNewlyExceededTools(laterPoll));
    }

    private static StatusSnapshot Snapshot(params (string Name, double Percent)[] tools)
    {
        return new StatusSnapshot(
            tools.Select(tool => new ToolStatus(
                tool.Name,
                ToolState.Active,
                0,
                0,
                1,
                new ToolQuota(tool.Percent, null, null, QuotaFreshness.Live))).ToList(),
            DateTime.UtcNow);
    }
}
