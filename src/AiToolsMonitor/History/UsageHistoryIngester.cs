using System.Globalization;
using System.Text.Json;
using AiToolsMonitor.Reports;
using Microsoft.Data.Sqlite;

namespace AiToolsMonitor.History;

/// <summary>
/// Reads Claude Code JSONL transcripts, Hermes SQLite, and OpenCode SQLite,
/// then upserts today's aggregated usage into HistoryDatabase.
/// Throttled to run at most once every 5 minutes.
/// </summary>
public sealed class UsageHistoryIngester
{
    private readonly HistoryDatabase _db;
    private readonly string _claudeProjectsRoot;
    private readonly string _hermesDbPath;
    private readonly string _openCodeDbPath;
    private DateTime _lastIngestUtc = DateTime.MinValue;
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);

    public UsageHistoryIngester(
        HistoryDatabase db,
        string? claudeProjectsRoot = null,
        string? hermesDbPath = null,
        string? openCodeDbPath = null)
    {
        _db = db;
        _claudeProjectsRoot = claudeProjectsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
        _hermesDbPath = hermesDbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "hermes", "state.db");
        _openCodeDbPath = openCodeDbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "opencode", "opencode.db");
    }

    /// <summary>
    /// Runs ingestion for all sources if the throttle interval has elapsed.
    /// Returns true if ingestion actually ran.
    /// </summary>
    public bool Ingest(DateTimeOffset? now = null)
    {
        var utcNow = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        if (utcNow - _lastIngestUtc < ThrottleInterval)
            return false;

        _lastIngestUtc = utcNow;
        string today = utcNow.ToString("yyyy-MM-dd");

        IngestClaudeCode(today);
        IngestHermes(today, utcNow);
        IngestOpenCode(today, utcNow);

        return true;
    }

    private void IngestClaudeCode(string today)
    {
        try
        {
            if (!Directory.Exists(_claudeProjectsRoot)) return;

            // Reuse the same approach as ClaudeCodeQuotaReader: find the newest
            // JSONL transcript, read all lines, but sum today's usage instead
            // of taking only the last assistant message.
            var transcript = Directory
                .EnumerateFiles(_claudeProjectsRoot, "*.jsonl", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (transcript is null) return;

            long inputTokens = 0;
            long outputTokens = 0;
            var perModel = new Dictionary<string, (long Input, long Output)>(
                StringComparer.OrdinalIgnoreCase);

            using var stream = new FileStream(
                transcript.FullName, FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("type", out var type) ||
                        type.GetString() != "assistant")
                        continue;

                    if (!root.TryGetProperty("timestamp", out var ts) ||
                        ts.ValueKind != JsonValueKind.String ||
                        !DateTimeOffset.TryParse(ts.GetString(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal,
                            out var parsed) ||
                        parsed.ToUniversalTime().ToString("yyyy-MM-dd") != today)
                        continue;

                    if (root.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("usage", out var usage))
                    {
                        long messageInput = GetTokenCount(usage, "input_tokens");
                        long messageOutput = GetTokenCount(usage, "output_tokens");
                        inputTokens += messageInput;
                        outputTokens += messageOutput;

                        string model = msg.TryGetProperty("model", out var modelElement) &&
                                       modelElement.ValueKind == JsonValueKind.String &&
                                       !string.IsNullOrWhiteSpace(modelElement.GetString())
                            ? modelElement.GetString()!
                            : "unknown";
                        perModel.TryGetValue(model, out var aggregate);
                        perModel[model] = (
                            aggregate.Input + messageInput,
                            aggregate.Output + messageOutput);
                    }
                }
                catch (JsonException) { }
            }

            if (inputTokens > 0 || outputTokens > 0)
                _db.UpsertDailyAggregate("Claude Code", today,
                    inputTokens, outputTokens, 0, 1);

            foreach (var (model, usage) in perModel)
            {
                var cost = ModelPricingCatalog.CalculateCost(
                    model,
                    usage.Input,
                    usage.Output);
                _db.UpsertModelDailyAggregate(
                    "Claude Code",
                    model,
                    today,
                    usage.Input,
                    usage.Output,
                    cost.CostUsd,
                    cost.IsKnown);
            }
        }
        catch { }
    }

    private void IngestHermes(string today, DateTime utcNow)
    {
        try
        {
            if (!File.Exists(_hermesDbPath)) return;

            var dayStart = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day,
                0, 0, 0, DateTimeKind.Utc);
            double dayStartUnix = new DateTimeOffset(dayStart).ToUnixTimeSeconds();
            double dayEndUnix = dayStartUnix + 86400;

            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = _hermesDbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 1,
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();
            using (var tc = conn.CreateCommand())
            {
                tc.CommandText = "PRAGMA busy_timeout = 1000;";
                tc.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    COALESCE(SUM(input_tokens), 0),
                    COALESCE(SUM(output_tokens), 0),
                    COALESCE(SUM(CASE WHEN actual_cost_usd > 0 THEN actual_cost_usd ELSE estimated_cost_usd END), 0.0),
                    COUNT(DISTINCT session_id)
                FROM session_model_usage
                WHERE last_seen >= @dayStart AND last_seen < @dayEnd;
                """;
            cmd.Parameters.AddWithValue("@dayStart", dayStartUnix);
            cmd.Parameters.AddWithValue("@dayEnd", dayEndUnix);

            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    long input = reader.GetInt64(0);
                    long output = reader.GetInt64(1);
                    double cost = reader.GetDouble(2);
                    int sessions = reader.GetInt32(3);

                    if (input > 0 || output > 0)
                        _db.UpsertDailyAggregate("Hermes Agent", today,
                            input, output, cost, sessions);
                }
            }

            using var modelCmd = conn.CreateCommand();
            modelCmd.CommandText = """
                SELECT
                    model,
                    COALESCE(SUM(input_tokens), 0),
                    COALESCE(SUM(output_tokens), 0)
                FROM session_model_usage
                WHERE last_seen >= @dayStart AND last_seen < @dayEnd
                GROUP BY model;
                """;
            modelCmd.Parameters.AddWithValue("@dayStart", dayStartUnix);
            modelCmd.Parameters.AddWithValue("@dayEnd", dayEndUnix);
            using var modelReader = modelCmd.ExecuteReader();
            while (modelReader.Read())
            {
                string model = modelReader.GetString(0);
                long input = modelReader.GetInt64(1);
                long output = modelReader.GetInt64(2);
                var cost = ModelPricingCatalog.CalculateCost(model, input, output);
                _db.UpsertModelDailyAggregate(
                    "Hermes Agent",
                    model,
                    today,
                    input,
                    output,
                    cost.CostUsd,
                    cost.IsKnown);
            }
        }
        catch { }
    }

    private void IngestOpenCode(string today, DateTime utcNow)
    {
        try
        {
            if (!File.Exists(_openCodeDbPath)) return;

            var dayStart = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day,
                0, 0, 0, DateTimeKind.Utc);
            long dayStartMs = new DateTimeOffset(dayStart).ToUnixTimeMilliseconds();
            long dayEndMs = dayStartMs + 86_400_000;

            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = _openCodeDbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 1,
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();
            using (var tc = conn.CreateCommand())
            {
                tc.CommandText = "PRAGMA busy_timeout = 1000;";
                tc.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    COALESCE(SUM(tokens_input), 0),
                    COALESCE(SUM(tokens_output), 0),
                    COALESCE(SUM(cost), 0.0),
                    COUNT(DISTINCT id)
                FROM session
                WHERE time_updated >= @dayStartMs AND time_updated < @dayEndMs;
                """;
            cmd.Parameters.AddWithValue("@dayStartMs", dayStartMs);
            cmd.Parameters.AddWithValue("@dayEndMs", dayEndMs);

            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    long input = reader.GetInt64(0);
                    long output = reader.GetInt64(1);
                    double cost = reader.GetDouble(2);
                    int sessions = reader.GetInt32(3);

                    if (input > 0 || output > 0)
                        _db.UpsertDailyAggregate("OpenCode", today,
                            input, output, cost, sessions);
                }
            }

            using var modelCmd = conn.CreateCommand();
            modelCmd.CommandText = """
                SELECT
                    COALESCE(NULLIF(model, ''), 'unknown'),
                    COALESCE(SUM(tokens_input), 0),
                    COALESCE(SUM(tokens_output), 0)
                FROM session
                WHERE time_updated >= @dayStartMs AND time_updated < @dayEndMs
                GROUP BY COALESCE(NULLIF(model, ''), 'unknown');
                """;
            modelCmd.Parameters.AddWithValue("@dayStartMs", dayStartMs);
            modelCmd.Parameters.AddWithValue("@dayEndMs", dayEndMs);
            using var modelReader = modelCmd.ExecuteReader();
            while (modelReader.Read())
            {
                string model = NormalizeOpenCodeModel(modelReader.GetString(0));
                long input = modelReader.GetInt64(1);
                long output = modelReader.GetInt64(2);
                var cost = ModelPricingCatalog.CalculateCost(model, input, output);
                _db.UpsertModelDailyAggregate(
                    "OpenCode",
                    model,
                    today,
                    input,
                    output,
                    cost.CostUsd,
                    cost.IsKnown);
            }
        }
        catch { }
    }

    private static string NormalizeOpenCodeModel(string model)
    {
        if (!model.StartsWith('{'))
            return model;

        try
        {
            using var document = JsonDocument.Parse(model);
            if (document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
            {
                return id.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Some OpenCode versions store a plain model name instead of JSON.
        }

        return model;
    }

    private static long GetTokenCount(JsonElement usage, string propertyName)
    {
        return usage.TryGetProperty(propertyName, out var value) &&
               value.TryGetInt64(out var tokens)
            ? tokens
            : 0;
    }
}
