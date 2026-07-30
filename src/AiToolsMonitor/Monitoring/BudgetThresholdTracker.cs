namespace AiToolsMonitor.Monitoring;

public sealed class BudgetThresholdTracker
{
    private readonly HashSet<string> _notifiedTools = new(StringComparer.Ordinal);

    public IReadOnlyList<ToolStatus> GetNewlyExceededTools(StatusSnapshot snapshot)
    {
        return snapshot.Tools
            .Where(tool =>
                tool.Quota?.PrimaryPercent >= 90 &&
                _notifiedTools.Add(tool.DisplayName))
            .ToList();
    }
}
