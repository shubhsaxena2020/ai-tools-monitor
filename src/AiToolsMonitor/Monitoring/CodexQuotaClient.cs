using System.Diagnostics;
using System.Text.Json;

namespace AiToolsMonitor.Monitoring;

public static class CodexQuotaClient
{
    public static async Task<ToolQuota> GetQuotaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c codex app-server",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // 3s was measured too tight for a real round trip (initialize + a
            // rate-limits read that hits a remote API) — 10s verified sufficient
            // in direct manual testing, 2026-07-29.
            cts.CancelAfter(10000);

            // Send initialize request
            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"AiToolsMonitor\",\"version\":\"1.0.0\"}}}".AsMemory(),
                cts.Token);
            await process.StandardInput.FlushAsync(cts.Token);

            // Send account/rateLimits/read request
            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}".AsMemory(),
                cts.Token);
            await process.StandardInput.FlushAsync(cts.Token);

            double? primaryPct = null;
            double? secondaryPct = null;
            int? primaryWindowMins = null;
            int? secondaryWindowMins = null;
            DateTimeOffset? resetsAt = null;
            bool success = false;

            while (!cts.Token.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cts.Token);
                if (line == null) break;

                if (line.Contains("\"id\":2") || line.Contains("\"rateLimits\""))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("result", out var result))
                        {
                            if (result.TryGetProperty("rateLimits", out var rateLimits) && rateLimits.ValueKind == JsonValueKind.Object)
                            {
                                ParseLimitObject(rateLimits, "primary", ref primaryPct, ref primaryWindowMins, ref resetsAt);
                                ParseLimitObject(rateLimits, "secondary", ref secondaryPct, ref secondaryWindowMins, ref resetsAt);
                                success = true;
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore non-JSON or partial notification lines
                    }
                }
            }

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch { }

            if (success && (primaryPct.HasValue || secondaryPct.HasValue))
            {
                return new ToolQuota(
                    primaryPct,
                    secondaryPct,
                    resetsAt,
                    QuotaFreshness.Live,
                    PrimaryWindowMinutes: primaryWindowMins,
                    SecondaryWindowMinutes: secondaryWindowMins);
            }

            return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
        }
        catch
        {
            return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
        }
    }

    private static void ParseLimitObject(JsonElement parent, string propName, ref double? pct, ref int? windowMinutes, ref DateTimeOffset? resetsAt)
    {
        if (parent.TryGetProperty(propName, out var limitObj) && limitObj.ValueKind == JsonValueKind.Object)
        {
            if (limitObj.TryGetProperty("usedPercent", out var usedPctProp) && usedPctProp.TryGetDouble(out var uVal))
            {
                pct = uVal;
            }
            else if (limitObj.TryGetProperty("used_percent", out var uVal2) && uVal2.TryGetDouble(out var uVal2Num))
            {
                pct = uVal2Num;
            }

            if (limitObj.TryGetProperty("windowDurationMins", out var windowProp) && windowProp.TryGetInt32(out var windowVal))
            {
                windowMinutes = windowVal;
            }

            if (!resetsAt.HasValue && limitObj.TryGetProperty("resetsAt", out var resetsProp))
            {
                if (resetsProp.ValueKind == JsonValueKind.Number && resetsProp.TryGetInt64(out var sec))
                {
                    resetsAt = DateTimeOffset.FromUnixTimeSeconds(sec);
                }
                else if (resetsProp.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(resetsProp.GetString(), out var dto))
                {
                    resetsAt = dto;
                }
            }
        }
    }
}
