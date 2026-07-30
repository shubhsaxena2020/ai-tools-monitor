namespace AiToolsMonitor.Budget;

/// <summary>
/// Computes rolling statistics over the last 14 days of daily cost
/// and flags today as anomalous if its z-score exceeds 2.
/// This is a passive insight surfaced in the budget edit window, not an alert storm.
/// </summary>
public sealed class CostAnomalyDetector
{
    private readonly History.HistoryDatabase _historyDb;
    private const int WindowDays = 14;
    private const double ZScoreThreshold = 2.0;

    public CostAnomalyDetector(History.HistoryDatabase historyDb)
    {
        _historyDb = historyDb;
    }

    /// <summary>
    /// Returns (isAnomaly, todayCost, mean, stddev, zScore).
    /// If there's insufficient history (&lt; 3 days), returns false for isAnomaly.
    /// </summary>
    public (bool IsAnomaly, double TodayCost, double Mean, double StdDev, double ZScore) Detect()
    {
        var today = DateTime.UtcNow.Date;
        var todayStr = today.ToString("yyyy-MM-dd");
        var (_, todayCost) = _historyDb.GetTodaySummary();

        // Gather last 14 days of cost (excluding today)
        var costs = new List<double>();
        for (int i = 1; i <= WindowDays; i++)
        {
            var date = today.AddDays(-i).ToString("yyyy-MM-dd");
            var (_, cost) = _historyDb.GetSummaryForDate(date);
            if (cost > 0)
                costs.Add(cost);
        }

        // Need at least 3 data points for meaningful statistics
        if (costs.Count < 3)
            return (false, todayCost, 0, 0, 0);

        double mean = costs.Average();
        double variance = costs.Sum(c => (c - mean) * (c - mean)) / costs.Count;
        double stddev = Math.Sqrt(variance);

        if (stddev < 1e-9)
            return (false, todayCost, mean, stddev, 0);

        double zScore = (todayCost - mean) / stddev;
        bool isAnomaly = zScore > ZScoreThreshold && todayCost > 0;

        return (isAnomaly, todayCost, mean, stddev, zScore);
    }
}