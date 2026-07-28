namespace AiToolsMonitor.Monitoring;

public enum ToolState { Idle, Quiet, Active }

public sealed record ToolStatus(string DisplayName, ToolState State, double CpuPercent, double RamMb, int ProcessCount);

public sealed record StatusSnapshot(IReadOnlyList<ToolStatus> Tools, DateTime SampledAtUtc)
{
    public int RunningCount => Tools.Count(t => t.State != ToolState.Idle);
}
