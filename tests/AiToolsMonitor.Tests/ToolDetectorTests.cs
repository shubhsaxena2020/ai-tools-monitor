using AiToolsMonitor.Monitoring;

namespace AiToolsMonitor.Tests;

public class ToolDetectorTests
{
    [Fact]
    public void AllToolsIdle_WhenNoProcessesMatch()
    {
        var snapshot = ToolDetector.Aggregate(new List<ProcessSample>
        {
            new(1, "explorer", "explorer.exe", 0, 50),
        });

        Assert.Equal(ToolProfile.Defaults.Length, snapshot.Tools.Count);
        Assert.All(snapshot.Tools, t => Assert.Equal(ToolState.Idle, t.State));
        Assert.Equal(0, snapshot.RunningCount);
    }

    [Fact]
    public void ClaudeCode_DetectedAsQuiet_WhenBelowActiveThreshold()
    {
        var snapshot = ToolDetector.Aggregate(new List<ProcessSample>
        {
            new(100, "claude", "claude.exe --resume", CpuPercent: 1.0, RamMb: 300),
        });

        var claude = snapshot.Tools.Single(t => t.DisplayName == "Claude Code");
        Assert.Equal(ToolState.Quiet, claude.State);
        Assert.Equal(1, claude.ProcessCount);
    }

    [Fact]
    public void ClaudeCode_DetectedAsActive_AtOrAboveThreshold()
    {
        var snapshot = ToolDetector.Aggregate(new List<ProcessSample>
        {
            new(100, "node", "node C:\\claude\\cli.js", CpuPercent: ToolDetector.ActiveCpuThresholdPercent, RamMb: 500),
        });

        var claude = snapshot.Tools.Single(t => t.DisplayName == "Claude Code");
        Assert.Equal(ToolState.Active, claude.State);
    }

    [Fact]
    public void MatchingIsCommandLineAware_NotJustProcessName()
    {
        // The real bug this guards against: several of these tools run as a
        // generic host process (node.exe, python.exe) with the real identity
        // only visible in the command line, not the process name.
        var snapshot = ToolDetector.Aggregate(new List<ProcessSample>
        {
            new(1, "node", "node.exe C:\\...\\opencode.cmd run \"do thing\"", 5, 200),
        });

        var opencode = snapshot.Tools.Single(t => t.DisplayName == "OpenCode");
        Assert.NotEqual(ToolState.Idle, opencode.State);
    }

    [Fact]
    public void MultipleProcessesForSameTool_AreAggregated()
    {
        var snapshot = ToolDetector.Aggregate(new List<ProcessSample>
        {
            new(1, "hermes", "hermes.exe -z task1", 2, 100),
            new(2, "hermes", "hermes.exe -z task2", 4, 150),
        });

        var hermes = snapshot.Tools.Single(t => t.DisplayName == "Hermes Agent");
        Assert.Equal(2, hermes.ProcessCount);
        Assert.Equal(6, hermes.CpuPercent);
        Assert.Equal(250, hermes.RamMb);
        Assert.Equal(ToolState.Active, hermes.State);
    }

    [Fact]
    public void RunningCount_CountsDistinctToolsNotProcesses()
    {
        var snapshot = ToolDetector.Aggregate(new List<ProcessSample>
        {
            new(1, "hermes", "hermes.exe task1", 2, 100),
            new(2, "hermes", "hermes.exe task2", 2, 100),
            new(3, "codex", "codex.exe exec", 2, 100),
        });

        Assert.Equal(2, snapshot.RunningCount); // Hermes + Codex, not 3 processes
    }

    [Fact]
    public void EmptyProcessList_ProducesAllIdleWithoutThrowing()
    {
        var snapshot = ToolDetector.Aggregate(new List<ProcessSample>());
        Assert.Equal(0, snapshot.RunningCount);
    }
}
