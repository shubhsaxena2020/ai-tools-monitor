namespace AiToolsMonitor.Monitoring;

public enum ToolState { Idle, Quiet, Active }

public enum QuotaFreshness { Live, Stale, Unavailable }

public enum QuotaDisplayKind { Percentage, Usage }

public sealed record ToolQuota(
    double? PrimaryPercent,
    double? SecondaryPercent,
    DateTimeOffset? ResetsAt,
    QuotaFreshness Freshness,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? CacheTokens = null,
    long? ReasoningTokens = null,
    double? CostUsd = null,
    DateTimeOffset? ObservedAt = null,
    QuotaDisplayKind DisplayKind = QuotaDisplayKind.Percentage
)
{
    public long? TotalTokens =>
        InputTokens.HasValue || OutputTokens.HasValue
            ? (InputTokens ?? 0) + (OutputTokens ?? 0)
            : null;
}

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
