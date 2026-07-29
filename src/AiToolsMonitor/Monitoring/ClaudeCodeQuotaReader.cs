using System.Text.Json;

namespace AiToolsMonitor.Monitoring;

public static class ClaudeCodeQuotaReader
{
    private static readonly string CaptureFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "ai-monitor",
        "statusline-capture.jsonl");

    public static ToolQuota GetQuota()
    {
        try
        {
            if (!File.Exists(CaptureFilePath))
            {
                return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
            }

            var fileInfo = new FileInfo(CaptureFilePath);
            var age = DateTime.UtcNow - fileInfo.LastWriteTimeUtc;

            string[] lines = File.ReadAllLines(CaptureFilePath);
            string? lastLine = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));

            if (string.IsNullOrWhiteSpace(lastLine))
            {
                return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
            }

            // Each line is "<ISO 8601 timestamp> <raw json>", not JSON on its own.
            int jsonStart = lastLine.IndexOf('{');
            if (jsonStart < 0)
            {
                return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
            }

            using var doc = JsonDocument.Parse(lastLine[jsonStart..]);
            var root = doc.RootElement;

            if (!root.TryGetProperty("rate_limits", out var rateLimits) ||
                rateLimits.ValueKind != JsonValueKind.Object)
            {
                return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
            }

            double? primaryPct = ExtractUsedPercentage(rateLimits, "five_hour");
            double? secondaryPct = ExtractUsedPercentage(rateLimits, "seven_day");
            DateTimeOffset? resetsAt =
                ExtractResetsAt(rateLimits, "five_hour") ??
                ExtractResetsAt(rateLimits, "seven_day");

            if (!primaryPct.HasValue && !secondaryPct.HasValue)
            {
                return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
            }

            QuotaFreshness freshness = age > TimeSpan.FromHours(1)
                ? QuotaFreshness.Stale
                : age > TimeSpan.FromMinutes(15)
                    ? QuotaFreshness.Stale
                    : QuotaFreshness.Live;

            return new ToolQuota(primaryPct, secondaryPct, resetsAt, freshness);
        }
        catch
        {
            return new ToolQuota(null, null, null, QuotaFreshness.Unavailable);
        }
    }

    private static double? ExtractUsedPercentage(JsonElement rateLimits, string bucket)
    {
        if (rateLimits.TryGetProperty(bucket, out var b) &&
            b.ValueKind == JsonValueKind.Object &&
            b.TryGetProperty("used_percentage", out var val) &&
            val.TryGetDouble(out var d))
        {
            return d;
        }
        return null;
    }

    private static DateTimeOffset? ExtractResetsAt(JsonElement rateLimits, string bucket)
    {
        if (rateLimits.TryGetProperty(bucket, out var b) &&
            b.ValueKind == JsonValueKind.Object &&
            b.TryGetProperty("resets_at", out var val))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetInt64(out var sec))
                return DateTimeOffset.FromUnixTimeSeconds(sec);

            if (val.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(val.GetString(), out var dto))
                return dto;
        }
        return null;
    }
}
