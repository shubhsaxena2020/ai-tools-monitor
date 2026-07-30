using AiToolsMonitor.History;
using Microsoft.Data.Sqlite;

namespace AiToolsMonitor.Tests;

public class UsageHistoryTests
{
    [Fact]
    public void HistoryDatabase_CreatesSchemaOnFirstUse()
    {
        using var temp = new TempDirectory();
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");

        using (var db = new HistoryDatabase(dbPath))
        {
            db.EnsureCreated();
        }

        // Verify the table exists by querying it
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name IN ('usage_history', 'model_usage_history');
            """;
        Assert.Equal(2L, cmd.ExecuteScalar());
    }

    [Fact]
    public void HistoryDatabase_UpsertDailyAggregate_IsIdempotent()
    {
        using var temp = new TempDirectory();
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        // First upsert
        db.UpsertDailyAggregate("Claude Code", "2026-07-30", 100, 50, 0.10, 1);

        // Second upsert with same key — should replace, not duplicate
        db.UpsertDailyAggregate("Claude Code", "2026-07-30", 200, 80, 0.25, 2);

        var (tokens, cost) = db.GetSummaryForDate("2026-07-30");
        Assert.Equal(280, tokens); // 200 + 80
        Assert.Equal(0.25, cost);
    }

    [Fact]
    public void HistoryDatabase_ModelUsageUpsertIsIdempotentAndQueryableByDate()
    {
        using var temp = new TempDirectory();
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        db.UpsertModelDailyAggregate(
            "Claude Code", "claude-sonnet-5", "2026-07-30",
            100, 20, 0.0006, pricingKnown: true);
        db.UpsertModelDailyAggregate(
            "Claude Code", "claude-sonnet-5", "2026-07-30",
            250, 50, 0.0015, pricingKnown: true);
        db.UpsertModelDailyAggregate(
            "Hermes Agent", "unmapped-model", "2026-07-30",
            400, 80, 0, pricingKnown: false);

        var models = db.GetModelUsage("2026-07-30", "2026-07-30");

        Assert.Equal(2, models.Count);
        var sonnet = Assert.Single(models, model => model.Model == "claude-sonnet-5");
        Assert.Equal(300, sonnet.TotalTokens);
        Assert.Equal(0.0015, sonnet.CostUsd, precision: 8);
        Assert.True(sonnet.PricingKnown);
        var unknown = Assert.Single(models, model => model.Model == "unmapped-model");
        Assert.False(unknown.PricingKnown);
    }

    [Fact]
    public void HistoryDatabase_GetTodaySummary_ReturnsZero_WhenEmpty()
    {
        using var temp = new TempDirectory();
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var (tokens, cost) = db.GetTodaySummary();
        Assert.Equal(0, tokens);
        Assert.Equal(0.0, cost);
    }

    [Fact]
    public void HistoryDatabase_GetTodaySummary_SumsAllTools()
    {
        using var temp = new TempDirectory();
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        db.UpsertDailyAggregate("Claude Code", today, 1000, 500, 0.50, 3);
        db.UpsertDailyAggregate("Hermes Agent", today, 2000, 800, 1.20, 5);

        var (tokens, cost) = db.GetTodaySummary();
        Assert.Equal(4300, tokens); // 1500 + 2800
        Assert.Equal(1.70, cost);
    }

    [Fact]
    public void HistoryDatabase_DifferentDaysAreSeparate()
    {
        using var temp = new TempDirectory();
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        db.UpsertDailyAggregate("Claude Code", "2026-07-29", 500, 200, 0.30, 2);
        db.UpsertDailyAggregate("Claude Code", "2026-07-30", 1000, 400, 0.60, 4);

        var (day1Tokens, day1Cost) = db.GetSummaryForDate("2026-07-29");
        var (day2Tokens, day2Cost) = db.GetSummaryForDate("2026-07-30");

        Assert.Equal(700, day1Tokens);
        Assert.Equal(0.30, day1Cost);
        Assert.Equal(1400, day2Tokens);
        Assert.Equal(0.60, day2Cost);
    }

    [Fact]
    public void UsageHistoryIngester_ClaudeCode_AggregatesTodayMessages()
    {
        using var temp = new TempDirectory();

        // Set up Claude Code JSONL source
        string claudeDir = System.IO.Path.Combine(temp.Path, "claude", "projects", "myproject");
        Directory.CreateDirectory(claudeDir);
        string transcript = System.IO.Path.Combine(claudeDir, "session.jsonl");

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string todayTimestamp = $"{today}T12:00:00Z";
        string yesterdayTimestamp = "2026-01-01T12:00:00Z";

        File.WriteAllLines(transcript,
        [
            "{\"type\":\"user\",\"timestamp\":\"" + todayTimestamp + "\",\"message\":{}}",
            "{\"type\":\"assistant\",\"timestamp\":\"" + todayTimestamp + "\",\"message\":{\"model\":\"claude-sonnet-5\",\"usage\":{\"input_tokens\":100,\"output_tokens\":30}}}",
            "{\"type\":\"assistant\",\"timestamp\":\"" + todayTimestamp + "\",\"message\":{\"model\":\"claude-haiku-4-5-20251001\",\"usage\":{\"input_tokens\":200,\"output_tokens\":60}}}",
            "{\"type\":\"assistant\",\"timestamp\":\"" + yesterdayTimestamp + "\",\"message\":{\"model\":\"claude\",\"usage\":{\"input_tokens\":999,\"output_tokens\":999}}}",
        ]);
        File.SetLastWriteTimeUtc(transcript, DateTime.UtcNow);

        // Set up history DB
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var ingester = new UsageHistoryIngester(db,
            claudeProjectsRoot: claudeDir + "\\..\\..\\..",
            hermesDbPath: System.IO.Path.Combine(temp.Path, "no-hermes.db"),
            openCodeDbPath: System.IO.Path.Combine(temp.Path, "no-opencode.db"));
        bool ran = ingester.Ingest(DateTimeOffset.UtcNow);

        Assert.True(ran);

        var (tokens, cost) = db.GetSummaryForDate(today);
        // Only today's messages: (100+30) + (200+60) = 390
        Assert.Equal(390, tokens);
        var models = db.GetModelUsage(today, today);
        Assert.Equal(2, models.Count);
        Assert.All(models, model => Assert.True(model.PricingKnown));
        Assert.All(models, model => Assert.True(model.CostUsd > 0));
    }

    [Fact]
    public void UsageHistoryIngester_Hermes_AggregatesTodayRows()
    {
        using var temp = new TempDirectory();

        // Set up Hermes DB with today's and yesterday's data
        string hermesDb = System.IO.Path.Combine(temp.Path, "state.db");
        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        double todayUnix = new DateTimeOffset(todayStart).ToUnixTimeSeconds();
        double yesterdayUnix = todayUnix - 86400;

        using (var conn = new SqliteConnection($"Data Source={hermesDb}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE session_model_usage (
                    session_id TEXT NOT NULL,
                    model TEXT NOT NULL,
                    input_tokens INTEGER NOT NULL,
                    output_tokens INTEGER NOT NULL,
                    cache_read_tokens INTEGER NOT NULL,
                    cache_write_tokens INTEGER NOT NULL,
                    estimated_cost_usd REAL NOT NULL,
                    actual_cost_usd REAL NOT NULL,
                    last_seen REAL
                );
                INSERT INTO session_model_usage VALUES
                    ('s1', 'model-a', 100, 20, 0, 0, 0.10, 0.08, :today1),
                    ('s1', 'model-a', 50, 10, 0, 0, 0.05, 0.04, :today2),
                    ('s2', 'model-b', 200, 40, 0, 0, 0.20, 0.18, :today3),
                    ('old', 'model-a', 999, 999, 0, 0, 1.00, 0.90, :yesterday);
                """;
            cmd.Parameters.AddWithValue(":today1", todayUnix + 3600);
            cmd.Parameters.AddWithValue(":today2", todayUnix + 7200);
            cmd.Parameters.AddWithValue(":today3", todayUnix + 10800);
            cmd.Parameters.AddWithValue(":yesterday", yesterdayUnix + 3600);
            cmd.ExecuteNonQuery();
        }

        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var historyDb = new HistoryDatabase(dbPath);
        historyDb.EnsureCreated();

        var ingester = new UsageHistoryIngester(historyDb,
            claudeProjectsRoot: System.IO.Path.Combine(temp.Path, "no-claude"),
            hermesDbPath: hermesDb,
            openCodeDbPath: System.IO.Path.Combine(temp.Path, "no-opencode.db"));
        ingester.Ingest(DateTimeOffset.UtcNow);

        string today = now.ToString("yyyy-MM-dd");
        var (tokens, cost) = historyDb.GetSummaryForDate(today);
        // Today: (100+20) + (50+10) + (200+40) = 420 tokens
        // Cost: 0.08 + 0.04 + 0.18 = 0.30
        Assert.Equal(420, tokens);
        Assert.Equal(0.30, cost, 2);
        Assert.Equal(2, historyDb.GetModelUsage(today, today).Count);
    }

    [Fact]
    public void UsageHistoryIngester_OpenCode_AggregatesTodayRows()
    {
        using var temp = new TempDirectory();

        // Set up OpenCode DB
        string ocDb = System.IO.Path.Combine(temp.Path, "opencode.db");
        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        long todayMs = new DateTimeOffset(todayStart).ToUnixTimeMilliseconds();
        long yesterdayMs = todayMs - 86_400_000;

        using (var conn = new SqliteConnection($"Data Source={ocDb}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE session (
                    id TEXT PRIMARY KEY,
                    directory TEXT NOT NULL,
                    cost REAL NOT NULL DEFAULT 0,
                    tokens_input INTEGER NOT NULL DEFAULT 0,
                    tokens_output INTEGER NOT NULL DEFAULT 0,
                    tokens_reasoning INTEGER NOT NULL DEFAULT 0,
                    tokens_cache_read INTEGER NOT NULL DEFAULT 0,
                    tokens_cache_write INTEGER NOT NULL DEFAULT 0,
                    model TEXT,
                    time_updated INTEGER NOT NULL
                );
                INSERT INTO session VALUES
                    ('s1', '/proj1', 0.15, 300, 50, 0, 0, 0, '{"id":"posiden/mimo-v2.5","providerID":"wtf17","variant":"default"}', :today1),
                    ('s2', '/proj2', 0.25, 500, 80, 0, 0, 0, 'ares/deepseek-v4-pro', :today2),
                    ('old', '/old', 1.00, 999, 999, 0, 0, 0, 'gpt-4o', :yesterday);
                """;
            cmd.Parameters.AddWithValue(":today1", todayMs + 3_600_000);
            cmd.Parameters.AddWithValue(":today2", todayMs + 7_200_000);
            cmd.Parameters.AddWithValue(":yesterday", yesterdayMs + 3_600_000);
            cmd.ExecuteNonQuery();
        }

        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var historyDb = new HistoryDatabase(dbPath);
        historyDb.EnsureCreated();

        var ingester = new UsageHistoryIngester(historyDb,
            claudeProjectsRoot: System.IO.Path.Combine(temp.Path, "no-claude"),
            hermesDbPath: System.IO.Path.Combine(temp.Path, "no-hermes.db"),
            openCodeDbPath: ocDb);
        ingester.Ingest(DateTimeOffset.UtcNow);

        string today = now.ToString("yyyy-MM-dd");
        var (tokens, cost) = historyDb.GetSummaryForDate(today);
        // Today: (300+50) + (500+80) = 930 tokens, cost: 0.15+0.25 = 0.40
        Assert.Equal(930, tokens);
        Assert.Equal(0.40, cost, 2);
        var models = historyDb.GetModelUsage(today, today);
        Assert.Equal(2, models.Count);
        Assert.All(models, model => Assert.True(model.PricingKnown));
        Assert.Contains(models, model => model.Model == "posiden/mimo-v2.5");
    }

    [Fact]
    public void UsageHistoryIngester_SkipsOldMessages()
    {
        using var temp = new TempDirectory();

        string claudeDir = System.IO.Path.Combine(temp.Path, "claude", "projects", "proj");
        Directory.CreateDirectory(claudeDir);
        string transcript = System.IO.Path.Combine(claudeDir, "session.jsonl");

        // Only yesterday's messages
        File.WriteAllLines(transcript,
        [
            """{"type":"assistant","timestamp":"2026-01-01T12:00:00Z","message":{"model":"claude","usage":{"input_tokens":500,"output_tokens":200}}}""",
        ]);
        File.SetLastWriteTimeUtc(transcript, DateTime.UtcNow);

        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var ingester = new UsageHistoryIngester(db,
            claudeProjectsRoot: claudeDir + "\\..\\..\\..",
            hermesDbPath: System.IO.Path.Combine(temp.Path, "no-hermes.db"),
            openCodeDbPath: System.IO.Path.Combine(temp.Path, "no-opencode.db"));
        ingester.Ingest(DateTimeOffset.UtcNow);

        // Today's row should not exist
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var (tokens, _) = db.GetSummaryForDate(today);
        Assert.Equal(0, tokens);
    }

    [Fact]
    public void UsageHistoryIngester_ThrottlesSubsequentCalls()
    {
        using var temp = new TempDirectory();
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var ingester = new UsageHistoryIngester(db);

        var now = DateTimeOffset.UtcNow;
        Assert.True(ingester.Ingest(now));
        Assert.False(ingester.Ingest(now.AddMinutes(2)));
        Assert.True(ingester.Ingest(now.AddMinutes(6)));
    }

    [Fact]
    public void UsageHistoryIngester_GracefullySkipsMissingSource()
    {
        using var temp = new TempDirectory();
        string dbPath = System.IO.Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        // All source paths point to nonexistent files
        var ingester = new UsageHistoryIngester(db,
            claudeProjectsRoot: System.IO.Path.Combine(temp.Path, "nonexistent"),
            hermesDbPath: System.IO.Path.Combine(temp.Path, "missing.db"),
            openCodeDbPath: System.IO.Path.Combine(temp.Path, "missing2.db"));

        // Should not throw
        bool ran = ingester.Ingest(DateTimeOffset.UtcNow);
        Assert.True(ran);

        // No data should be written
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var (tokens, cost) = db.GetSummaryForDate(today);
        Assert.Equal(0, tokens);
        Assert.Equal(0.0, cost);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AiToolsMonitor.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Path, recursive: true);
        }
    }
}
