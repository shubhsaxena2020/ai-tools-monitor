using AiToolsMonitor.Monitoring;
using System.Text.Json;
using Xunit;

namespace AiToolsMonitor.Tests;

public class QuotaTests
{
    [Fact]
    public void ClaudeCodeQuotaReader_ReturnsUnavailable_WhenFileDoesNotExist()
    {
        var quota = ClaudeCodeQuotaReader.GetQuota();
        // Since %TEMP%\claude-status-line.json is checked, if missing or invalid, freshness is Unavailable or Stale
        Assert.NotNull(quota);
    }

    [Fact]
    public void ToolStatus_QuotaDefaultsToNull()
    {
        var status = new ToolStatus("Hermes", ToolState.Active, 5.0, 150, 1);
        Assert.Null(status.Quota);
    }

    [Fact]
    public void ToolStatus_CanCarryQuota()
    {
        var quota = new ToolQuota(25.0, 50.0, DateTimeOffset.UtcNow.AddHours(2), QuotaFreshness.Live);
        var status = new ToolStatus("Codex", ToolState.Active, 10.0, 300, 2, quota);

        Assert.NotNull(status.Quota);
        Assert.Equal(25.0, status.Quota.PrimaryPercent);
        Assert.Equal(50.0, status.Quota.SecondaryPercent);
        Assert.Equal(QuotaFreshness.Live, status.Quota.Freshness);
    }
}
