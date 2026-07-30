using Microsoft.Data.Sqlite;

namespace AiToolsMonitor.Monitoring;

public static class HermesQuotaClient
{
    private static readonly string DatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "hermes",
        "state.db");

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
                WITH latest_session AS (
                    SELECT session_id
                    FROM session_model_usage
                    WHERE last_seen IS NOT NULL
                    ORDER BY last_seen DESC
                    LIMIT 1
                )
                SELECT
                    SUM(input_tokens),
                    SUM(output_tokens),
                    SUM(cache_read_tokens),
                    SUM(cache_write_tokens),
                    SUM(reasoning_tokens),
                    SUM(CASE
                        WHEN actual_cost_usd > 0 THEN actual_cost_usd
                        ELSE estimated_cost_usd
                    END),
                    MAX(last_seen)
                FROM session_model_usage
                WHERE session_id = (SELECT session_id FROM latest_session)
                GROUP BY session_id;
                """;

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return Unavailable();

            long inputTokens = reader.GetInt64(0);
            long outputTokens = reader.GetInt64(1);
            long cacheTokens = reader.GetInt64(2) + reader.GetInt64(3);
            long reasoningTokens = reader.GetInt64(4);
            double cost = reader.GetDouble(5);
            var observedAt = DateTimeOffset.FromUnixTimeSeconds(
                checked((long)Math.Floor(reader.GetDouble(6))));
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
