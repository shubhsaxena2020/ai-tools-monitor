namespace AiToolsMonitor.Monitoring;

public enum ToolState { Idle, Quiet, Active }

public enum QuotaFreshness { Live, Stale, Unavailable }

public sealed record ToolQuota(
    double? PrimaryPercent,
    double? SecondaryPercent,
    DateTimeOffset? ResetsAt,
    QuotaFreshness Freshness
);

public sealed record ToolStatus(
    string DisplayName,
    ToolState State,
    double CpuPercent,
    double RamMb,
    int ProcessCount,
    ToolQuota? Quota = null
);

public sealed record StatusSnapshot(IReadOnlyList<ToolStatus> Tools, DateTime SampledAtUtc)
{
    public int RunningCount => Tools.Count(t => t.State != ToolState.Idle);
}

