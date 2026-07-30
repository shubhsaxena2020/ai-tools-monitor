using AiToolsMonitor.Monitoring;
using Microsoft.Data.Sqlite;

namespace AiToolsMonitor.Tests;

public class QuotaTests
{
    [Fact]
    public void ClaudeCodeQuotaReader_UsesNewestTranscriptAndLastAssistantUsage()
    {
        using var temp = new TempDirectory();
        string olderProject = Directory.CreateDirectory(Path.Combine(temp.Path, "older")).FullName;
        string activeProject = Directory.CreateDirectory(Path.Combine(temp.Path, "active")).FullName;
        string olderTranscript = Path.Combine(olderProject, "older.jsonl");
        string activeTranscript = Path.Combine(activeProject, "active.jsonl");
        var now = new DateTimeOffset(2026, 7, 30, 8, 0, 0, TimeSpan.Zero);

        File.WriteAllText(olderTranscript,
            """{"type":"assistant","timestamp":"2026-07-30T07:00:00Z","message":{"usage":{"input_tokens":999,"output_tokens":999}}}""");
        File.SetLastWriteTimeUtc(olderTranscript, now.UtcDateTime.AddHours(-1));

        File.WriteAllLines(activeTranscript,
        [
            """{"type":"user","timestamp":"2026-07-30T07:55:00Z","message":{}}""",
            """{"type":"assistant","timestamp":"2026-07-30T07:56:00Z","message":{"model":"claude","usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":30,"cache_creation_input_tokens":10}}}""",
            """{"type":"assistant","timestamp":"2026-07-30T07:58:00Z","message":{"model":"claude","usage":{"input_tokens":200,"output_tokens":40,"cache_read_input_tokens":5,"cache_creation_input_tokens":0}}}"""
        ]);
        File.SetLastWriteTimeUtc(activeTranscript, now.UtcDateTime.AddMinutes(-2));

        var quota = ClaudeCodeQuotaReader.GetQuota(temp.Path, now);

        Assert.Equal(QuotaDisplayKind.Usage, quota.DisplayKind);
        Assert.Equal(QuotaFreshness.Live, quota.Freshness);
        Assert.Equal(200, quota.InputTokens);
        Assert.Equal(40, quota.OutputTokens);
        Assert.Equal(5, quota.CacheTokens);
        Assert.Equal(240, quota.TotalTokens);
        Assert.Equal(new DateTimeOffset(2026, 7, 30, 7, 58, 0, TimeSpan.Zero), quota.ObservedAt);
    }

    [Fact]
    public void ClaudeCodeQuotaReader_ReturnsStaleForOldTranscript()
    {
        using var temp = new TempDirectory();
        string project = Directory.CreateDirectory(Path.Combine(temp.Path, "project")).FullName;
        string transcript = Path.Combine(project, "session.jsonl");
        var now = new DateTimeOffset(2026, 7, 30, 8, 0, 0, TimeSpan.Zero);

        File.WriteAllText(transcript,
            """{"type":"assistant","timestamp":"2026-07-30T07:30:00Z","message":{"usage":{"input_tokens":10,"output_tokens":5}}}""");
        File.SetLastWriteTimeUtc(transcript, now.UtcDateTime.AddMinutes(-16));

        var quota = ClaudeCodeQuotaReader.GetQuota(temp.Path, now);

        Assert.Equal(QuotaFreshness.Stale, quota.Freshness);
        Assert.Equal(15, quota.TotalTokens);
    }

    [Fact]
    public void HermesQuotaClient_AggregatesAllUsageRowsFromLatestSession()
    {
        using var temp = new TempDirectory();
        string dbPath = Path.Combine(temp.Path, "state.db");
        var now = new DateTimeOffset(2026, 7, 30, 8, 0, 0, TimeSpan.Zero);
        using (var connection = OpenDatabase(dbPath))
        {
            Execute(connection, """
                CREATE TABLE session_model_usage (
                    session_id TEXT NOT NULL,
                    model TEXT NOT NULL,
                    input_tokens INTEGER NOT NULL,
                    output_tokens INTEGER NOT NULL,
                    cache_read_tokens INTEGER NOT NULL,
                    cache_write_tokens INTEGER NOT NULL,
                    reasoning_tokens INTEGER NOT NULL,
                    estimated_cost_usd REAL NOT NULL,
                    actual_cost_usd REAL NOT NULL,
                    last_seen REAL
                );
                INSERT INTO session_model_usage VALUES
                    ('old', 'model-a', 1, 2, 3, 4, 5, 0.10, 0.09, 1785394800),
                    ('new', 'model-b', 200, 40, 30, 10, 15, 0.50, 0.45, 1785398340),
                    ('new', 'model-c', 100, 10, 5, 0, 7, 0.20, 0.00, 1785398330);
                """);
        }

        var quota = HermesQuotaClient.GetQuota(dbPath, now);

        Assert.Equal(QuotaDisplayKind.Usage, quota.DisplayKind);
        Assert.Equal(QuotaFreshness.Live, quota.Freshness);
        Assert.Equal(300, quota.InputTokens);
        Assert.Equal(50, quota.OutputTokens);
        Assert.Equal(45, quota.CacheTokens);
        Assert.Equal(22, quota.ReasoningTokens);
        Assert.Equal(0.65, quota.CostUsd);
        Assert.Equal(new DateTimeOffset(2026, 7, 30, 7, 59, 0, TimeSpan.Zero), quota.ObservedAt);
    }

    [Fact]
    public void OpenCodeQuotaClient_UsesMostRecentlyUpdatedSession()
    {
        using var temp = new TempDirectory();
        string dbPath = Path.Combine(temp.Path, "opencode.db");
        var now = new DateTimeOffset(2026, 7, 30, 8, 0, 0, TimeSpan.Zero);
        using (var connection = OpenDatabase(dbPath))
        {
            Execute(connection, """
                CREATE TABLE session (
                    id TEXT PRIMARY KEY,
                    directory TEXT NOT NULL,
                    cost REAL NOT NULL DEFAULT 0,
                    tokens_input INTEGER NOT NULL DEFAULT 0,
                    tokens_output INTEGER NOT NULL DEFAULT 0,
                    tokens_reasoning INTEGER NOT NULL DEFAULT 0,
                    tokens_cache_read INTEGER NOT NULL DEFAULT 0,
                    tokens_cache_write INTEGER NOT NULL DEFAULT 0,
                    time_updated INTEGER NOT NULL
                );
                INSERT INTO session VALUES
                    ('old', 'C:/old', 0.10, 1, 2, 3, 4, 5, 1785394800000),
                    ('new', 'C:/new', 0.25, 300, 50, 20, 10, 5, 1785398340000);
                """);
        }

        var quota = OpenCodeQuotaClient.GetQuota(dbPath, now);

        Assert.Equal(QuotaDisplayKind.Usage, quota.DisplayKind);
        Assert.Equal(QuotaFreshness.Live, quota.Freshness);
        Assert.Equal(300, quota.InputTokens);
        Assert.Equal(50, quota.OutputTokens);
        Assert.Equal(20, quota.ReasoningTokens);
        Assert.Equal(15, quota.CacheTokens);
        Assert.Equal(0.25, quota.CostUsd);
    }

    [Fact]
    public void LocalQuotaClients_ReturnUnavailable_WhenSourceDoesNotExist()
    {
        using var temp = new TempDirectory();
        string missing = Path.Combine(temp.Path, "missing");
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(QuotaFreshness.Unavailable, ClaudeCodeQuotaReader.GetQuota(missing, now).Freshness);
        Assert.Equal(QuotaFreshness.Unavailable, HermesQuotaClient.GetQuota(missing, now).Freshness);
        Assert.Equal(QuotaFreshness.Unavailable, OpenCodeQuotaClient.GetQuota(missing, now).Freshness);
    }

    [Fact]
    public void ToolStatus_QuotaDefaultsToNull()
    {
        var status = new ToolStatus("Hermes", ToolState.Active, 5.0, 150, 1);
        Assert.Null(status.Quota);
    }

    [Fact]
    public void ToolStatus_CanCarryPercentageQuota()
    {
        var quota = new ToolQuota(25.0, 50.0, DateTimeOffset.UtcNow.AddHours(2), QuotaFreshness.Live);
        var status = new ToolStatus("Codex", ToolState.Active, 10.0, 300, 2, quota);

        Assert.NotNull(status.Quota);
        Assert.Equal(25.0, status.Quota.PrimaryPercent);
        Assert.Equal(50.0, status.Quota.SecondaryPercent);
        Assert.Equal(QuotaDisplayKind.Percentage, status.Quota.DisplayKind);
    }

    private static SqliteConnection OpenDatabase(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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
