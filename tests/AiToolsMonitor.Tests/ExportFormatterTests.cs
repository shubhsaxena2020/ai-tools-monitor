using System.Text.Json;
using AiToolsMonitor.Export;
using AiToolsMonitor.Monitoring;

namespace AiToolsMonitor.Tests;

public class ExportFormatterTests
{
    [Fact]
    public void ToCsv_WritesOneRowPerToolWithEverySnapshotStatusAndQuotaField()
    {
        var snapshot = CreateSnapshot();

        string csv = StatusSnapshotFormatter.ToCsv(snapshot);

        string[] lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal(
            "SampledAtUtc,DisplayName,State,CpuPercent,RamMb,ProcessCount,PrimaryPercent,SecondaryPercent,ResetsAt,Freshness,InputTokens,OutputTokens,CacheTokens,ReasoningTokens,CostUsd,ObservedAt,DisplayKind,PrimaryWindowMinutes,SecondaryWindowMinutes",
            lines[0]);
        Assert.Equal(
            "2026-07-30T08:00:00.0000000Z,\"Claude, Code\",Active,12.5,345.75,2,91.25,44.5,2026-07-30T09:00:00.0000000+00:00,Live,100,20,30,5,1.25,2026-07-30T07:59:00.0000000+00:00,Percentage,300,10080",
            lines[1]);
        Assert.Equal(
            "2026-07-30T08:00:00.0000000Z,Hermes Agent,Idle,0,0,0,,,,,,,,,,,,,",
            lines[2]);
    }

    [Fact]
    public void ToJson_SerializesTheCurrentSnapshot()
    {
        var snapshot = CreateSnapshot();

        string json = StatusSnapshotFormatter.ToJson(snapshot);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("2026-07-30T08:00:00Z", document.RootElement.GetProperty("sampledAtUtc").GetString());
        var tools = document.RootElement.GetProperty("tools");
        Assert.Equal(2, tools.GetArrayLength());
        Assert.Equal("Claude, Code", tools[0].GetProperty("displayName").GetString());
        Assert.Equal(5, tools[0].GetProperty("quota").GetProperty("reasoningTokens").GetInt64());
        Assert.Equal(JsonValueKind.Null, tools[1].GetProperty("quota").ValueKind);
    }

    private static StatusSnapshot CreateSnapshot()
    {
        var quota = new ToolQuota(
            91.25,
            44.5,
            new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero),
            QuotaFreshness.Live,
            InputTokens: 100,
            OutputTokens: 20,
            CacheTokens: 30,
            ReasoningTokens: 5,
            CostUsd: 1.25,
            ObservedAt: new DateTimeOffset(2026, 7, 30, 7, 59, 0, TimeSpan.Zero),
            DisplayKind: QuotaDisplayKind.Percentage,
            PrimaryWindowMinutes: 300,
            SecondaryWindowMinutes: 10080);

        return new StatusSnapshot(
        [
            new ToolStatus("Claude, Code", ToolState.Active, 12.5, 345.75, 2, quota),
            new ToolStatus("Hermes Agent", ToolState.Idle, 0, 0, 0),
        ],
        new DateTime(2026, 7, 30, 8, 0, 0, DateTimeKind.Utc));
    }
}
