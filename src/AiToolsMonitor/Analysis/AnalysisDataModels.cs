namespace AiToolsMonitor.Analysis;

public enum HealthStatus
{
    Pass,
    Warning,
    Fail
}

public sealed record TaskCategoryBreakdown(
    string Category,
    int SessionCount,
    double Percentage);

public sealed record OneShotMetrics(
    int TotalEdits,
    int RetryEdits,
    int OneShotEdits,
    double SuccessRatePercentage,
    int EvaluatedSessionsCount);

public sealed record ToolEfficiencyRow(
    string ToolName,
    int SessionCount,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalTokens,
    long TokensPerSession,
    double TotalCostUsd,
    double CostPerSession,
    double CostPer100kTokens);

public sealed record HealthCheckResult(
    string Name,
    HealthStatus Status,
    string Reason);

public sealed record SetupHealthGrade(
    string Grade,
    int Score,
    IReadOnlyList<HealthCheckResult> Checks);

public sealed record AnalysisReport(
    IReadOnlyList<TaskCategoryBreakdown> Categories,
    OneShotMetrics OneShotMetrics,
    IReadOnlyList<ToolEfficiencyRow> ToolEfficiencies,
    SetupHealthGrade HealthGrade,
    DateTimeOffset GeneratedAt);
