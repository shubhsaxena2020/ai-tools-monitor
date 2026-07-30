using Microsoft.Data.Sqlite;

namespace AiToolsMonitor.History;

public sealed record ModelUsageSummary(
    string Tool,
    string Model,
    long InputTokens,
    long OutputTokens,
    double CostUsd,
    bool PricingKnown)
{
    public long TotalTokens => InputTokens + OutputTokens;

    public double? CostPerMillionTokens =>
        PricingKnown && TotalTokens > 0
            ? CostUsd / TotalTokens * 1_000_000d
            : null;
}

public sealed record DailyCostTotal(string Date, double CostUsd);

/// <summary>
/// Owns the local history.db that preserves usage data beyond Claude Code's
/// 30-day session deletion. One row per (tool, date) aggregate.
/// </summary>
public sealed class HistoryDatabase : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public HistoryDatabase(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiToolsMonitor",
            "history.db");
    }

    /// <summary>Creates the database file and schema if they don't exist.</summary>
    public void EnsureCreated()
    {
        try
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS usage_history (
                    date TEXT NOT NULL,
                    tool TEXT NOT NULL,
                    input_tokens INTEGER NOT NULL DEFAULT 0,
                    output_tokens INTEGER NOT NULL DEFAULT 0,
                    cost_usd REAL NOT NULL DEFAULT 0,
                    session_count INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (date, tool)
                );

                CREATE TABLE IF NOT EXISTS model_usage_history (
                    date TEXT NOT NULL,
                    tool TEXT NOT NULL,
                    model TEXT NOT NULL,
                    input_tokens INTEGER NOT NULL DEFAULT 0,
                    output_tokens INTEGER NOT NULL DEFAULT 0,
                    cost_usd REAL NOT NULL DEFAULT 0,
                    pricing_known INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (date, tool, model)
                );

                CREATE TABLE IF NOT EXISTS session_history (
                    session_id TEXT NOT NULL,
                    tool TEXT NOT NULL,
                    start_utc TEXT NOT NULL,
                    end_utc TEXT NOT NULL,
                    total_tokens INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (session_id, tool)
                );
                """;
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best effort — never crash the app
        }
    }

    /// <summary>
    /// Inserts or replaces one model's daily aggregate for a tool.
    /// Idempotent: safe to call every poll tick.
    /// </summary>
    public void UpsertModelDailyAggregate(
        string tool,
        string model,
        string date,
        long inputTokens,
        long outputTokens,
        double costUsd,
        bool pricingKnown)
    {
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO model_usage_history
                    (date, tool, model, input_tokens, output_tokens, cost_usd, pricing_known)
                VALUES
                    (@date, @tool, @model, @inputTokens, @outputTokens, @costUsd, @pricingKnown)
                ON CONFLICT(date, tool, model) DO UPDATE SET
                    input_tokens = @inputTokens,
                    output_tokens = @outputTokens,
                    cost_usd = @costUsd,
                    pricing_known = @pricingKnown;
                """;
            cmd.Parameters.AddWithValue("@date", date);
            cmd.Parameters.AddWithValue("@tool", tool);
            cmd.Parameters.AddWithValue("@model", model);
            cmd.Parameters.AddWithValue("@inputTokens", inputTokens);
            cmd.Parameters.AddWithValue("@outputTokens", outputTokens);
            cmd.Parameters.AddWithValue("@costUsd", costUsd);
            cmd.Parameters.AddWithValue("@pricingKnown", pricingKnown ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best effort
        }
    }

    public IReadOnlyList<ModelUsageSummary> GetModelUsage(
        string startDate,
        string endDate)
    {
        var results = new List<ModelUsageSummary>();
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    tool,
                    model,
                    COALESCE(SUM(input_tokens), 0),
                    COALESCE(SUM(output_tokens), 0),
                    COALESCE(SUM(cost_usd), 0.0),
                    MIN(pricing_known)
                FROM model_usage_history
                WHERE date >= @startDate AND date <= @endDate
                GROUP BY tool, model
                ORDER BY cost_usd DESC, model;
                """;
            cmd.Parameters.AddWithValue("@startDate", startDate);
            cmd.Parameters.AddWithValue("@endDate", endDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new ModelUsageSummary(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetDouble(4),
                    reader.GetInt64(5) != 0));
            }
        }
        catch
        {
            // Best effort
        }

        return results;
    }

    public IReadOnlyList<DailyCostTotal> GetDailyModelCosts(
        string startDate,
        string endDate)
    {
        var results = new List<DailyCostTotal>();
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT date, COALESCE(SUM(cost_usd), 0.0)
                FROM model_usage_history
                WHERE date >= @startDate AND date <= @endDate
                GROUP BY date
                ORDER BY date;
                """;
            cmd.Parameters.AddWithValue("@startDate", startDate);
            cmd.Parameters.AddWithValue("@endDate", endDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(new DailyCostTotal(reader.GetString(0), reader.GetDouble(1)));
        }
        catch
        {
            // Best effort
        }

        return results;
    }

    /// <summary>
    /// Inserts or replaces the daily aggregate for a tool.
    /// Idempotent: safe to call every poll tick.
    /// </summary>
    public void UpsertDailyAggregate(
        string tool, string date,
        long inputTokens, long outputTokens,
        double costUsd, int sessionCount)
    {
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO usage_history (date, tool, input_tokens, output_tokens, cost_usd, session_count)
                VALUES (@date, @tool, @inputTokens, @outputTokens, @costUsd, @sessionCount)
                ON CONFLICT(date, tool) DO UPDATE SET
                    input_tokens = @inputTokens,
                    output_tokens = @outputTokens,
                    cost_usd = @costUsd,
                    session_count = @sessionCount;
                """;
            cmd.Parameters.AddWithValue("@date", date);
            cmd.Parameters.AddWithValue("@tool", tool);
            cmd.Parameters.AddWithValue("@inputTokens", inputTokens);
            cmd.Parameters.AddWithValue("@outputTokens", outputTokens);
            cmd.Parameters.AddWithValue("@costUsd", costUsd);
            cmd.Parameters.AddWithValue("@sessionCount", sessionCount);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>Returns the aggregated totals for today (UTC date).</summary>
    public (long totalTokens, double totalCost) GetTodaySummary()
    {
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COALESCE(SUM(input_tokens + output_tokens), 0),
                       COALESCE(SUM(cost_usd), 0.0)
                FROM usage_history
                WHERE date = @date;
                """;
            cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt64(0), reader.GetDouble(1));
        }
        catch
        {
            // Best effort
        }
        return (0, 0.0);
    }

    /// <summary>Returns the aggregated totals for an arbitrary date.</summary>
    public (long totalTokens, double totalCost) GetSummaryForDate(string date)
    {
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COALESCE(SUM(input_tokens + output_tokens), 0),
                       COALESCE(SUM(cost_usd), 0.0)
                FROM usage_history
                WHERE date = @date;
                """;
            cmd.Parameters.AddWithValue("@date", date);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt64(0), reader.GetDouble(1));
        }
        catch
        {
            // Best effort
        }
        return (0, 0.0);
    }

    /// <summary>
    /// Returns aggregated usage across all recorded days for each tool.
    /// </summary>
    public List<AiToolsMonitor.Analysis.ToolEfficiencyRow> GetToolEfficiencySummary()
    {
        var list = new List<AiToolsMonitor.Analysis.ToolEfficiencyRow>();
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT tool,
                       COALESCE(SUM(input_tokens), 0),
                       COALESCE(SUM(output_tokens), 0),
                       COALESCE(SUM(cost_usd), 0.0),
                       COALESCE(SUM(session_count), 0)
                FROM usage_history
                GROUP BY tool;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string toolName = reader.GetString(0);
                long input = reader.GetInt64(1);
                long output = reader.GetInt64(2);
                double cost = reader.GetDouble(3);
                int sessions = reader.GetInt32(4);

                long totalTokens = input + output;
                long tokensPerSession = sessions > 0 ? totalTokens / sessions : 0;
                double costPerSession = sessions > 0 ? cost / sessions : 0.0;
                double costPer100k = totalTokens > 0 ? (cost / totalTokens) * 100_000.0 : 0.0;

                list.Add(new AiToolsMonitor.Analysis.ToolEfficiencyRow(
                    toolName,
                    sessions,
                    input,
                    output,
                    totalTokens,
                    tokensPerSession,
                    cost,
                    costPerSession,
                    costPer100k));
            }
        }
        catch
        {
            // Best effort
        }
        return list;
    }

    /// <summary>Returns per-date totals within [startDate, endDate] inclusive, ordered by date.</summary>
    public List<(string date, long totalTokens, double totalCost)> GetUsageSummaryForDateRange(
        string startDate, string endDate)
    {
        var results = new List<(string date, long totalTokens, double totalCost)>();
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT date,
                       COALESCE(SUM(input_tokens + output_tokens), 0) AS total_tokens,
                       COALESCE(SUM(cost_usd), 0.0) AS total_cost
                FROM usage_history
                WHERE date >= @start AND date <= @end
                GROUP BY date
                ORDER BY date;
                """;
            cmd.Parameters.AddWithValue("@start", startDate);
            cmd.Parameters.AddWithValue("@end", endDate);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add((reader.GetString(0), reader.GetInt64(1), reader.GetDouble(2)));
        }
        catch
        {
            // Best effort
        }
        return results;
    }

    /// <summary>Returns per-tool totals within [startDate, endDate] inclusive, ordered by tool name.</summary>
    public List<(string tool, long totalTokens, double totalCost, int sessionCount)> GetDailyBreakdownByTool(
        string startDate, string endDate)
    {
        var results = new List<(string tool, long totalTokens, double totalCost, int sessionCount)>();
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT tool,
                       COALESCE(SUM(input_tokens + output_tokens), 0) AS total_tokens,
                       COALESCE(SUM(cost_usd), 0.0) AS total_cost,
                       COALESCE(SUM(session_count), 0) AS total_sessions
                FROM usage_history
                WHERE date >= @start AND date <= @end
                GROUP BY tool
                ORDER BY tool;
                """;
            cmd.Parameters.AddWithValue("@start", startDate);
            cmd.Parameters.AddWithValue("@end", endDate);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add((reader.GetString(0), reader.GetInt64(1), reader.GetDouble(2), reader.GetInt32(3)));
        }
        catch
        {
            // Best effort
        }
        return results;
    }

    /// <summary>Returns daily total tokens for the last N days (for the heatmap).</summary>
    public List<(string date, long totalTokens)> GetDailyActivityForHeatmap(int daysBack = 90)
    {
        var results = new List<(string date, long totalTokens)>();
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT date,
                       COALESCE(SUM(input_tokens + output_tokens), 0) AS total_tokens
                FROM usage_history
                WHERE date >= @start
                GROUP BY date
                ORDER BY date;
                """;
            string startDate = DateTime.UtcNow.AddDays(-daysBack).ToString("yyyy-MM-dd");
            cmd.Parameters.AddWithValue("@start", startDate);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add((reader.GetString(0), reader.GetInt64(1)));
        }
        catch
        {
            // Best effort
        }
        return results;
    }

    /// <summary>Upserts a session boundary record (idempotent).</summary>
    public void UpsertSession(
        string sessionId, string tool,
        string startUtc, string endUtc, long totalTokens)
    {
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO session_history (session_id, tool, start_utc, end_utc, total_tokens)
                VALUES (@sessionId, @tool, @startUtc, @endUtc, @totalTokens)
                ON CONFLICT(session_id, tool) DO UPDATE SET
                    start_utc = @startUtc,
                    end_utc = @endUtc,
                    total_tokens = @totalTokens;
                """;
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            cmd.Parameters.AddWithValue("@tool", tool);
            cmd.Parameters.AddWithValue("@startUtc", startUtc);
            cmd.Parameters.AddWithValue("@endUtc", endUtc);
            cmd.Parameters.AddWithValue("@totalTokens", totalTokens);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>Returns sessions whose start date falls within [startDate, endDate], newest first.</summary>
    public List<(string sessionId, string tool, string startUtc, string endUtc, long totalTokens)>
        GetSessionsForDateRange(string startDate, string endDate)
    {
        var results = new List<(string sessionId, string tool, string startUtc, string endUtc, long totalTokens)>();
        try
        {
            var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT session_id, tool, start_utc, end_utc, total_tokens
                FROM session_history
                WHERE date(start_utc) >= @start AND date(start_utc) <= @end
                ORDER BY start_utc DESC;
                """;
            cmd.Parameters.AddWithValue("@start", startDate);
            cmd.Parameters.AddWithValue("@end", endDate);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4)));
        }
        catch
        {
            // Best effort
        }
        return results;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private SqliteConnection OpenConnection()
    {
        if (_connection != null)
        {
            try
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                    return _connection;
            }
            catch { }
            _connection.Dispose();
            _connection = null;
        }

        var dir = Path.GetDirectoryName(_dbPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 2,
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA busy_timeout = 2000;";
            cmd.ExecuteNonQuery();
        }

        return _connection;
    }
}
