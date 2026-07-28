using System.Diagnostics;
using System.Management;

namespace AiToolsMonitor.Monitoring;

/// <summary>
/// Reads real process command lines via WMI (Win32_Process -- .NET's own Process
/// class does not expose command line on Windows without this) and turns CPU
/// time into a percentage using a delta against the previous sample, per PID.
/// </summary>
public sealed class ProcessEnumerator
{
    private readonly Dictionary<int, (TimeSpan CpuTime, DateTime SampledAt)> _previous = new();

    public IReadOnlyList<ProcessSample> Scan()
    {
        var commandLines = ReadCommandLines();
        var samples = new List<ProcessSample>();
        var now = DateTime.UtcNow;
        var seenPids = new HashSet<int>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var pid = proc.Id;
                seenPids.Add(pid);
                var cpuTime = proc.TotalProcessorTime;
                double cpuPercent = 0;

                if (_previous.TryGetValue(pid, out var prev))
                {
                    var elapsed = (now - prev.SampledAt).TotalSeconds;
                    if (elapsed > 0.05)
                    {
                        var cpuDelta = (cpuTime - prev.CpuTime).TotalSeconds;
                        cpuPercent = 100.0 * cpuDelta / (elapsed * Environment.ProcessorCount);
                        cpuPercent = Math.Clamp(cpuPercent, 0, 100 * Environment.ProcessorCount);
                    }
                }

                _previous[pid] = (cpuTime, now);

                var ramMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                commandLines.TryGetValue(pid, out var cmdLine);

                samples.Add(new ProcessSample(pid, proc.ProcessName, cmdLine ?? string.Empty, cpuPercent, ramMb));
            }
            catch (Exception)
            {
                // Process exited between enumeration and inspection, or access
                // denied (elevated process) -- skip it for this scan, per FR-9.
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Drop stale entries for processes that no longer exist, so the
        // dictionary doesn't grow unbounded over a long-running session.
        foreach (var staleP in _previous.Keys.Where(p => !seenPids.Contains(p)).ToList())
            _previous.Remove(staleP);

        return samples;
    }

    private static Dictionary<int, string> ReadCommandLines()
    {
        var result = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                var cmdLine = obj["CommandLine"] as string ?? string.Empty;
                result[pid] = cmdLine;
                obj.Dispose();
            }
        }
        catch (Exception)
        {
            // WMI can fail under restricted permissions -- degrade to
            // process-name-only matching rather than crashing the poller.
        }
        return result;
    }
}
