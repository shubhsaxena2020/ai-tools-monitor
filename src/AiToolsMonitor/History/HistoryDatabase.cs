using Microsoft.Data.Sqlite;

namespace AiToolsMonitor.History;

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
                """;
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best effort — never crash the app
        }
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
