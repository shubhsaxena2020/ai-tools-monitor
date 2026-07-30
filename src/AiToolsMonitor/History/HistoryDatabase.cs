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
