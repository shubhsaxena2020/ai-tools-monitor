namespace AiToolsMonitor.Monitoring;

/// <summary>Raw per-process sample as read from the OS (real source: WMI via ProcessEnumerator).</summary>
public sealed record ProcessSample(int Pid, string ProcessName, string CommandLine, double CpuPercent, double RamMb);

/// <summary>
/// Pure aggregation logic: matches raw process samples against tool profiles and
/// rolls them up into one ToolStatus per profile. Deliberately has no OS dependency
/// so it's fully unit-testable without a real process list.
/// </summary>
public static class ToolDetector
{
    public const double ActiveCpuThresholdPercent = 3.0;

    public static StatusSnapshot Aggregate(IReadOnlyList<ProcessSample> samples, IReadOnlyList<ToolProfile>? profiles = null)
    {
        profiles ??= ToolProfile.Defaults;
        var results = new List<ToolStatus>(profiles.Count);

        foreach (var profile in profiles)
        {
            var matched = samples.Where(s =>
                profile.Matches(s.ProcessName.ToLowerInvariant(), s.CommandLine.ToLowerInvariant())).ToList();

            if (matched.Count == 0)
            {
                results.Add(new ToolStatus(profile.DisplayName, ToolState.Idle, 0, 0, 0));
                continue;
            }

            var totalCpu = matched.Sum(m => m.CpuPercent);
            var totalRam = matched.Sum(m => m.RamMb);
            var state = totalCpu >= ActiveCpuThresholdPercent ? ToolState.Active : ToolState.Quiet;
            results.Add(new ToolStatus(profile.DisplayName, state, totalCpu, totalRam, matched.Count));
        }

        return new StatusSnapshot(results, DateTime.UtcNow);
    }
}
