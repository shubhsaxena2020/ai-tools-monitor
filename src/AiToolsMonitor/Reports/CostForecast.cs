namespace AiToolsMonitor.Reports;

public static class CostForecast
{
    public static double CalculateMonthEnd(
        IReadOnlyCollection<double> lastSevenDailyCosts,
        double monthToDateCost,
        DateTime asOf)
    {
        if (lastSevenDailyCosts.Count == 0)
            return Math.Max(0, monthToDateCost);

        double recentDailyAverage = lastSevenDailyCosts
            .Select(cost => Math.Max(0, cost))
            .Average();
        int remainingDays = Math.Max(
            0,
            DateTime.DaysInMonth(asOf.Year, asOf.Month) - asOf.Day);

        return Math.Max(0, monthToDateCost) + (recentDailyAverage * remainingDays);
    }
}
