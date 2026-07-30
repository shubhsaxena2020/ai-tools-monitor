using Microsoft.Data.Sqlite;

namespace AiToolsMonitor.Monitoring;

public static class OpenCodeQuotaClient
{
    private static readonly string DatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local",
        "share",
        "opencode",
        "opencode.db");

    private static readonly TimeSpan LiveThreshold = TimeSpan.FromMinutes(15);

    public static ToolQuota GetQuota(string? databasePath = null, DateTimeOffset? now = null)
    {
        try
        {
            string path = databasePath ?? DatabasePath;
            if (!File.Exists(path))
                return Unavailable();

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 1,
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using (var timeoutCommand = connection.CreateCommand())
            {
                timeoutCommand.CommandText = "PRAGMA busy_timeout = 1000;";
                timeoutCommand.ExecuteNonQuery();
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    tokens_input,
                    tokens_output,
                    tokens_reasoning,
                    tokens_cache_read,
                    tokens_cache_write,
                    cost,
                    time_updated
                FROM session
                ORDER BY time_updated DESC
                LIMIT 1;
                """;

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return Unavailable();

            long inputTokens = reader.GetInt64(0);
            long outputTokens = reader.GetInt64(1);
            long reasoningTokens = reader.GetInt64(2);
            long cacheTokens = reader.GetInt64(3) + reader.GetInt64(4);
            double cost = reader.GetDouble(5);
            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6));
            var freshness = GetFreshness(now ?? DateTimeOffset.UtcNow, observedAt);

            return new ToolQuota(
                null,
                null,
                null,
                freshness,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                CacheTokens: cacheTokens,
                ReasoningTokens: reasoningTokens,
                CostUsd: cost,
                ObservedAt: observedAt,
                DisplayKind: QuotaDisplayKind.Usage);
        }
        catch
        {
            return Unavailable();
        }
    }

    private static QuotaFreshness GetFreshness(DateTimeOffset now, DateTimeOffset observedAt)
    {
        return now - observedAt > LiveThreshold
            ? QuotaFreshness.Stale
            : QuotaFreshness.Live;
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
