using System.Globalization;
using System.Text.Json;

namespace AiToolsMonitor.Monitoring;

public static class ClaudeCodeQuotaReader
{
    private static readonly string ProjectsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "projects");

    private static readonly TimeSpan LiveThreshold = TimeSpan.FromMinutes(15);

    public static ToolQuota GetQuota()
    {
        return GetQuota(ProjectsRoot, DateTimeOffset.UtcNow);
    }

    public static ToolQuota GetQuota(string projectsRoot, DateTimeOffset now)
    {
        try
        {
            if (!Directory.Exists(projectsRoot))
                return Unavailable();

            var transcript = Directory
                .EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (transcript is null)
                return Unavailable();

            long inputTokens = 0;
            long outputTokens = 0;
            long cacheTokens = 0;
            DateTimeOffset? observedAt = null;
            bool foundUsage = false;

            using var stream = new FileStream(
                transcript.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;

                    if (!root.TryGetProperty("type", out var type) ||
                        type.GetString() != "assistant" ||
                        !root.TryGetProperty("message", out var message) ||
                        !message.TryGetProperty("usage", out var usage))
                    {
                        continue;
                    }

                    foundUsage = true;
                    inputTokens = GetTokenCount(usage, "input_tokens");
                    outputTokens = GetTokenCount(usage, "output_tokens");
                    cacheTokens =
                        GetTokenCount(usage, "cache_read_input_tokens") +
                        GetTokenCount(usage, "cache_creation_input_tokens");

                    if (root.TryGetProperty("timestamp", out var timestamp) &&
                        timestamp.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(
                            timestamp.GetString(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal,
                            out var parsedTimestamp))
                    {
                        observedAt = parsedTimestamp;
                    }
                }
                catch (JsonException)
                {
                    // The active transcript can end with a partially written line.
                }
            }

            if (!foundUsage)
                return Unavailable();

            var age = now.UtcDateTime - transcript.LastWriteTimeUtc;
            var freshness = age > LiveThreshold
                ? QuotaFreshness.Stale
                : QuotaFreshness.Live;

            return new ToolQuota(
                null,
                null,
                null,
                freshness,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                CacheTokens: cacheTokens,
                ObservedAt: observedAt,
                DisplayKind: QuotaDisplayKind.Usage);
        }
        catch
        {
            return Unavailable();
        }
    }

    private static long GetTokenCount(JsonElement usage, string propertyName)
    {
        return usage.TryGetProperty(propertyName, out var value) &&
               value.TryGetInt64(out var tokens)
            ? tokens
            : 0;
    }

    private static ToolQuota Unavailable()
    {
        return new ToolQuota(
            null,
            null,
            null,
            QuotaFreshness.Unavailable,
            DisplayKind: QuotaDisplayKind.Usage);
    }
}
