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
                SELECT
                    input_tokens,
                    output_tokens,
                    cache_read_tokens,
                    cache_write_tokens,
                    estimated_cost_usd,
                    actual_cost_usd,
                    last_seen
                FROM session_model_usage
                WHERE last_seen IS NOT NULL
                ORDER BY last_seen DESC
                LIMIT 1;
                """;

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return Unavailable();

            long inputTokens = reader.GetInt64(0);
            long outputTokens = reader.GetInt64(1);
            long cacheTokens = reader.GetInt64(2) + reader.GetInt64(3);
            double estimatedCost = reader.GetDouble(4);
            double actualCost = reader.GetDouble(5);
            var observedAt = DateTimeOffset.FromUnixTimeSeconds(
                checked((long)Math.Floor(reader.GetDouble(6))));
            double cost = actualCost > 0 ? actualCost : estimatedCost;
            var freshness = GetFreshness(now ?? DateTimeOffset.UtcNow, observedAt);

            return new ToolQuota(
                null,
                null,
                null,
                freshness,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                CacheTokens: cacheTokens,
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
