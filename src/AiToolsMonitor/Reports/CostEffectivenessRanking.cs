using AiToolsMonitor.History;

namespace AiToolsMonitor.Reports;

public sealed record ModelRankingRow(
    string Model,
    long TotalTokens,
    double CostUsd,
    double? CostPerMillionTokens);

public static class CostEffectivenessRanking
{
    public static IReadOnlyList<ModelRankingRow> Rank(
        IEnumerable<ModelUsageSummary> usage)
    {
        return usage
            .GroupBy(row => row.Model, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                long totalTokens = group.Sum(row => row.TotalTokens);
                double costUsd = group.Sum(row => row.CostUsd);
                bool pricingKnown = group.All(row => row.PricingKnown);
                double? costPerMillion = pricingKnown && totalTokens > 0
                    ? costUsd / totalTokens * 1_000_000d
                    : null;
                return new ModelRankingRow(
                    group.First().Model,
                    totalTokens,
                    costUsd,
                    costPerMillion);
            })
            .OrderBy(row => row.CostPerMillionTokens ?? double.MaxValue)
            .ThenBy(row => row.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
