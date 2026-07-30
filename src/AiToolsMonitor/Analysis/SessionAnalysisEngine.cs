using System.Globalization;
using System.Text.Json;
using AiToolsMonitor.History;

namespace AiToolsMonitor.Analysis;

public sealed class SessionAnalysisEngine
{
    private readonly HistoryDatabase _historyDb;
    private readonly string _claudeProjectsRoot;
    private readonly string _userProfileDir;

    public SessionAnalysisEngine(
        HistoryDatabase historyDb,
        string? claudeProjectsRoot = null,
        string? userProfileDir = null)
    {
        _historyDb = historyDb;
        _userProfileDir = userProfileDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _claudeProjectsRoot = claudeProjectsRoot ?? Path.Combine(_userProfileDir, ".claude", "projects");
    }

    public AnalysisReport GenerateReport()
    {
        var sessionSummaries = AnalyzeClaudeSessions();

        // 1. Feature 15: Category Breakdown
        var categories = ComputeCategoryBreakdown(sessionSummaries);

        // 2. Feature 16: One-Shot Success Metrics
        var oneShot = ComputeOneShotMetrics(sessionSummaries);

        // 3. Feature 17: Cross-Tool Cost Efficiency
        var efficiencies = ComputeToolEfficiency();

        // 4. Feature 20: Setup Health Grade
        int totalReReads = sessionSummaries.Sum(s => s.ReReadCount);
        var healthGrade = ComputeHealthGrade(oneShot.SuccessRatePercentage, totalReReads);

        return new AnalysisReport(
            categories,
            oneShot,
            efficiencies,
            healthGrade,
            DateTimeOffset.UtcNow);
    }

    public List<ParsedSessionSummary> AnalyzeClaudeSessions()
    {
        var results = new List<ParsedSessionSummary>();

        try
        {
            if (!Directory.Exists(_claudeProjectsRoot))
                return results;

            var files = Directory.EnumerateFiles(_claudeProjectsRoot, "*.jsonl", SearchOption.AllDirectories);

            foreach (var filePath in files)
            {
                try
                {
                    var summary = ParseSingleTranscript(filePath);
                    if (summary != null)
                    {
                        results.Add(summary);
                    }
                }
                catch
                {
                    // Best effort per session file
                }
            }
        }
        catch
        {
            // Best effort
        }

        return results;
    }

    public ParsedSessionSummary? ParseSingleTranscript(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            string sessionId = Path.GetFileNameWithoutExtension(filePath);
            int totalEdits = 0;
            int retriedEdits = 0;
            int reReadCount = 0;

            var editedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var readPathCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int testKeywordHits = 0;
            int debugKeywordHits = 0;
            int planKeywordHits = 0;
            int codingKeywordHits = 0;
            int editToolCalls = 0;
            int inspectToolCalls = 0;
            int bashToolCalls = 0;

            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    string? type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                    // Extract user keywords
                    if (type == "user")
                    {
                        string userText = ExtractTextFromElement(root);
                        if (!string.IsNullOrEmpty(userText))
                        {
                            AnalyzeUserText(userText, ref testKeywordHits, ref debugKeywordHits, ref planKeywordHits, ref codingKeywordHits);
                        }
                    }

                    // Extract tool calls from assistant or system messages
                    if (root.TryGetProperty("message", out var msgElement))
                    {
                        if (msgElement.TryGetProperty("content", out var contentArray) &&
                            contentArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var block in contentArray.EnumerateArray())
                            {
                                ProcessContentBlock(
                                    block,
                                    ref editToolCalls,
                                    ref inspectToolCalls,
                                    ref bashToolCalls,
                                    ref totalEdits,
                                    ref retriedEdits,
                                    editedPaths,
                                    readPathCounts,
                                    ref testKeywordHits,
                                    ref debugKeywordHits);
                            }
                        }
                    }
                    else if (root.TryGetProperty("content", out var rootContent) &&
                             rootContent.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in rootContent.EnumerateArray())
                        {
                            ProcessContentBlock(
                                block,
                                ref editToolCalls,
                                ref inspectToolCalls,
                                ref bashToolCalls,
                                ref totalEdits,
                                ref retriedEdits,
                                editedPaths,
                                readPathCounts,
                                ref testKeywordHits,
                                ref debugKeywordHits);
                        }
                    }
                }
                catch (JsonException)
                {
                    // Incomplete line
                }
            }

            // Calculate re-read instances (reads of same file beyond 2 times in session)
            foreach (var kvp in readPathCounts)
            {
                if (kvp.Value > 2)
                {
                    reReadCount += (kvp.Value - 2);
                }
            }

            string category = ClassifyCategory(
                testKeywordHits, debugKeywordHits, planKeywordHits, codingKeywordHits,
                editToolCalls, inspectToolCalls, bashToolCalls);

            return new ParsedSessionSummary(
                sessionId,
                category,
                totalEdits,
                retriedEdits,
                reReadCount);
        }
        catch
        {
            return null;
        }
    }

    private static void ProcessContentBlock(
        JsonElement block,
        ref int editToolCalls,
        ref int inspectToolCalls,
        ref int bashToolCalls,
        ref int totalEdits,
        ref int retriedEdits,
        HashSet<string> editedPaths,
        Dictionary<string, int> readPathCounts,
        ref int testKeywordHits,
        ref int debugKeywordHits)
    {
        if (!block.TryGetProperty("type", out var bType) || bType.GetString() != "tool_use")
            return;

        string toolName = block.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(toolName)) return;

        JsonElement inputObj = default;
        bool hasInput = block.TryGetProperty("input", out inputObj) && inputObj.ValueKind == JsonValueKind.Object;

        string targetPath = hasInput ? ExtractFilePath(inputObj) : "";

        if (IsEditTool(toolName))
        {
            editToolCalls++;
            totalEdits++;

            if (!string.IsNullOrEmpty(targetPath))
            {
                string normPath = NormalizePath(targetPath);
                if (editedPaths.Contains(normPath))
                {
                    retriedEdits++;
                }
                else
                {
                    editedPaths.Add(normPath);
                }
            }
        }
        else if (IsReadTool(toolName))
        {
            inspectToolCalls++;

            if (!string.IsNullOrEmpty(targetPath))
            {
                string normPath = NormalizePath(targetPath);
                readPathCounts[normPath] = readPathCounts.GetValueOrDefault(normPath, 0) + 1;
            }
        }
        else if (IsBashTool(toolName))
        {
            bashToolCalls++;

            if (hasInput && inputObj.TryGetProperty("command", out var cmdProp) && cmdProp.ValueKind == JsonValueKind.String)
            {
                string cmd = cmdProp.GetString() ?? "";
                if (IsTestCommand(cmd)) testKeywordHits += 2;
                if (IsDebugCommand(cmd)) debugKeywordHits += 2;
            }
        }
    }

    public static string ClassifyCategory(
        int testHits, int debugHits, int planHits, int codingHits,
        int editTools, int inspectTools, int bashTools)
    {
        if (testHits >= 2 || (testHits > 0 && bashTools > 0))
            return "Testing";

        if (debugHits >= 2 || (debugHits > 0 && (bashTools > 0 || editTools > 0)))
            return "Debugging";

        if (editTools > 0 || codingHits >= 2)
            return "Coding";

        if (inspectTools > 0 || planHits >= 2)
            return "Planning";

        return "General";
    }

    private static void AnalyzeUserText(
        string text,
        ref int testHits,
        ref int debugHits,
        ref int planHits,
        ref int codingHits)
    {
        string lower = text.ToLowerInvariant();

        if (lower.Contains("test") || lower.Contains("pytest") || lower.Contains("jest") || lower.Contains("assert") || lower.Contains("spec"))
            testHits++;

        if (lower.Contains("error") || lower.Contains("bug") || lower.Contains("fix") || lower.Contains("fail") || lower.Contains("exception") || lower.Contains("traceback"))
            debugHits++;

        if (lower.Contains("plan") || lower.Contains("design") || lower.Contains("architect") || lower.Contains("how to") || lower.Contains("todo"))
            planHits++;

        if (lower.Contains("implement") || lower.Contains("create") || lower.Contains("add") || lower.Contains("build") || lower.Contains("refactor"))
            codingHits++;
    }

    private static string ExtractTextFromElement(JsonElement element)
    {
        if (element.TryGetProperty("content", out var contentProp))
        {
            if (contentProp.ValueKind == JsonValueKind.String)
                return contentProp.GetString() ?? "";

            if (contentProp.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var item in contentProp.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                    {
                        sb.AppendLine(textProp.GetString());
                    }
                }
                return sb.ToString();
            }
        }

        if (element.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.Object)
        {
            return ExtractTextFromElement(msgProp);
        }

        return "";
    }

    private static bool IsEditTool(string toolName) =>
        toolName.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("Write", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("write_to_file", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("replace_file_content", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("multi_replace_file_content", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("NotebookEdit", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadTool(string toolName) =>
        toolName.Equals("View", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("Read", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("view_file", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("read_file", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("read_multiple_files", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("Grep", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("Glob", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("LS", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("list_dir", StringComparison.OrdinalIgnoreCase);

    private static bool IsBashTool(string toolName) =>
        toolName.Equals("Bash", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("run_command", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestCommand(string cmd) =>
        cmd.Contains("test", StringComparison.OrdinalIgnoreCase) ||
        cmd.Contains("pytest", StringComparison.OrdinalIgnoreCase) ||
        cmd.Contains("jest", StringComparison.OrdinalIgnoreCase) ||
        cmd.Contains("vitest", StringComparison.OrdinalIgnoreCase);

    private static bool IsDebugCommand(string cmd) =>
        cmd.Contains("debug", StringComparison.OrdinalIgnoreCase) ||
        cmd.Contains("trace", StringComparison.OrdinalIgnoreCase) ||
        cmd.Contains("log", StringComparison.OrdinalIgnoreCase);

    private static string ExtractFilePath(JsonElement inputObj)
    {
        string[] propNames = ["file_path", "path", "target_file", "TargetFile", "file", "AbsolutePath"];
        foreach (var p in propNames)
        {
            if (inputObj.TryGetProperty(p, out var val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString() ?? "";
            }
        }
        return "";
    }

    private static string NormalizePath(string p) =>
        p.Trim().ToLowerInvariant().Replace('\\', '/');

    private static List<TaskCategoryBreakdown> ComputeCategoryBreakdown(List<ParsedSessionSummary> sessions)
    {
        if (sessions.Count == 0)
        {
            return [
                new TaskCategoryBreakdown("Coding", 0, 0.0),
                new TaskCategoryBreakdown("Debugging", 0, 0.0),
                new TaskCategoryBreakdown("Testing", 0, 0.0),
                new TaskCategoryBreakdown("Planning", 0, 0.0),
                new TaskCategoryBreakdown("General", 0, 0.0)
            ];
        }

        var counts = sessions
            .GroupBy(s => s.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        string[] allCategories = ["Coding", "Debugging", "Testing", "Planning", "General"];
        int total = sessions.Count;

        return allCategories
            .Select(cat =>
            {
                int count = counts.GetValueOrDefault(cat, 0);
                double pct = total > 0 ? (count * 100.0 / total) : 0.0;
                return new TaskCategoryBreakdown(cat, count, pct);
            })
            .OrderByDescending(c => c.SessionCount)
            .ToList();
    }

    private static OneShotMetrics ComputeOneShotMetrics(List<ParsedSessionSummary> sessions)
    {
        int totalEdits = sessions.Sum(s => s.TotalEdits);
        int retryEdits = sessions.Sum(s => s.RetriedEdits);
        int oneShotEdits = totalEdits - retryEdits;
        double successRate = totalEdits > 0 ? (oneShotEdits * 100.0 / totalEdits) : 100.0;

        return new OneShotMetrics(
            totalEdits,
            retryEdits,
            oneShotEdits,
            Math.Round(successRate, 1),
            sessions.Count);
    }

    private List<ToolEfficiencyRow> ComputeToolEfficiency()
    {
        var dbRows = _historyDb.GetToolEfficiencySummary();

        var canonicalTools = new[] { "Claude Code", "Hermes Agent", "Codex", "OpenCode", "Antigravity" };
        var result = new List<ToolEfficiencyRow>();

        foreach (var tool in canonicalTools)
        {
            var dbMatch = dbRows.FirstOrDefault(r => r.ToolName.Equals(tool, StringComparison.OrdinalIgnoreCase));
            if (dbMatch != null)
            {
                result.Add(dbMatch);
            }
            else
            {
                result.Add(new ToolEfficiencyRow(tool, 0, 0, 0, 0, 0, 0.0, 0.0, 0.0));
            }
        }

        return result;
    }

    public SetupHealthGrade ComputeHealthGrade(double oneShotRate, int totalReReads)
    {
        var checks = new List<HealthCheckResult>();
        int score = 100;

        // Check 1: CLAUDE.md Health
        string claudeMdPath = Path.Combine(_userProfileDir, ".claude", "CLAUDE.md");
        if (File.Exists(claudeMdPath))
        {
            var fi = new FileInfo(claudeMdPath);
            double sizeKb = fi.Length / 1024.0;
            if (fi.Length <= 10_240) // <= 10 KB
            {
                checks.Add(new HealthCheckResult(
                    "CLAUDE.md Context Budget",
                    HealthStatus.Pass,
                    $"CLAUDE.md is present and optimal size ({sizeKb:0.1} KB)"));
            }
            else
            {
                score -= 10;
                checks.Add(new HealthCheckResult(
                    "CLAUDE.md Context Budget",
                    HealthStatus.Warning,
                    $"CLAUDE.md is large ({sizeKb:0.1} KB), which consumes context budget"));
            }
        }
        else
        {
            score -= 10;
            checks.Add(new HealthCheckResult(
                "CLAUDE.md Context Budget",
                HealthStatus.Warning,
                "CLAUDE.md not found in ~/.claude/ directory"));
        }

        // Check 2: Installed Skills/Agents Inventory
        var (totalCount, duplicateCount) = AuditSkillsAndAgents();
        if (duplicateCount > 0)
        {
            score -= 15;
            checks.Add(new HealthCheckResult(
                "Skills & Agents Inventory",
                HealthStatus.Warning,
                $"Found {totalCount} installed skills/agents with {duplicateCount} duplicate name conflicts"));
        }
        else
        {
            checks.Add(new HealthCheckResult(
                "Skills & Agents Inventory",
                HealthStatus.Pass,
                $"Found {totalCount} installed skills/agents with 0 name conflicts"));
        }

        // Check 3: Repeated File Re-reads Waste Signal
        if (totalReReads <= 5)
        {
            checks.Add(new HealthCheckResult(
                "File Re-Read Redundancy",
                HealthStatus.Pass,
                $"Low file re-read redundancy across sessions ({totalReReads} repeated re-reads)"));
        }
        else
        {
            score -= 15;
            checks.Add(new HealthCheckResult(
                "File Re-Read Redundancy",
                HealthStatus.Warning,
                $"Detected {totalReReads} file re-read instances across sessions (context bloat)"));
        }

        // Check 4: One-Shot Edit Retry Rate
        if (oneShotRate >= 80.0)
        {
            checks.Add(new HealthCheckResult(
                "Code Edit Success Rate",
                HealthStatus.Pass,
                $"High one-shot edit success rate ({oneShotRate:0.#}%)"));
        }
        else if (oneShotRate >= 60.0)
        {
            score -= 15;
            checks.Add(new HealthCheckResult(
                "Code Edit Success Rate",
                HealthStatus.Warning,
                $"Moderate edit retry rate ({100.0 - oneShotRate:0.#}% retries)"));
        }
        else
        {
            score -= 30;
            checks.Add(new HealthCheckResult(
                "Code Edit Success Rate",
                HealthStatus.Fail,
                $"High edit retry rate ({100.0 - oneShotRate:0.#}% retries)"));
        }

        score = Math.Max(0, Math.Min(100, score));

        string grade = score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            _ => "F"
        };

        return new SetupHealthGrade(grade, score, checks);
    }

    private (int totalCount, int duplicateCount) AuditSkillsAndAgents()
    {
        var names = new List<string>();
        string[] searchDirs = [
            Path.Combine(_userProfileDir, ".claude", "skills"),
            Path.Combine(_userProfileDir, ".claude", "agents"),
            Path.Combine(_userProfileDir, ".gemini", "config", "plugins")
        ];

        foreach (var dir in searchDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                    {
                        string name = Path.GetFileName(entry);
                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add(name.ToLowerInvariant());
                        }
                    }
                }
            }
            catch { }
        }

        int totalCount = names.Count;
        int duplicateCount = names
            .GroupBy(n => n)
            .Where(g => g.Count() > 1)
            .Sum(g => g.Count() - 1);

        return (totalCount, duplicateCount);
    }
}

public sealed record ParsedSessionSummary(
    string SessionId,
    string Category,
    int TotalEdits,
    int RetriedEdits,
    int ReReadCount);
