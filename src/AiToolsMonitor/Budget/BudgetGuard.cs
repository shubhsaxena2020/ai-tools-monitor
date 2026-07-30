namespace AiToolsMonitor.Budget;

/// <summary>
/// Checks today's accumulated cost against soft/hard caps from BudgetConfig.
/// Returns alert level without side effects — the caller (TrayHost) decides
/// how to present the warning (balloon tip, icon color, etc.).
/// </summary>
public enum BudgetAlertLevel
{
    None,
    SoftCapExceeded,
    HardCapExceeded,
}

public sealed class BudgetGuard
{
    private readonly BudgetConfig _config;
    private readonly History.HistoryDatabase _historyDb;
    private readonly HashSet<string> _softNotifiedDates = new();
    private readonly HashSet<string> _hardNotifiedDates = new();

    public BudgetGuard(BudgetConfig config, History.HistoryDatabase historyDb)
    {
        _config = config;
        _historyDb = historyDb;
    }

    /// <summary>
    /// Checks today's cost against caps. Returns the highest alert level.
    /// Tracks per-day notification state so each cap is only surfaced once per day.
    /// </summary>
    public BudgetAlertLevel CheckToday()
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var (_, todayCost) = _historyDb.GetTodaySummary();

        if (todayCost >= _config.HardCapUsd)
        {
            if (_hardNotifiedDates.Add(today))
                return BudgetAlertLevel.HardCapExceeded;
            return BudgetAlertLevel.None; // already notified today
        }

        if (todayCost >= _config.SoftCapUsd)
        {
            if (_softNotifiedDates.Add(today))
                return BudgetAlertLevel.SoftCapExceeded;
            return BudgetAlertLevel.None; // already notified today
        }

        return BudgetAlertLevel.None;
    }

    /// <summary>Forces a re-check on next poll (e.g. after config edit).</summary>
    public void ResetTodayNotifications()
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _softNotifiedDates.Remove(today);
        _hardNotifiedDates.Remove(today);
    }

    /// <summary>Returns current config (for UI display).</summary>
    public BudgetConfig Config => _config;
}