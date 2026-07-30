using AiToolsMonitor.Analysis;
using AiToolsMonitor.History;

namespace AiToolsMonitor.Tests;

public class AnalysisTests
{
    [Theory]
    [InlineData(2, 0, 0, 0, 0, 0, 1, "Testing")]
    [InlineData(0, 2, 0, 0, 0, 0, 1, "Debugging")]
    [InlineData(0, 0, 0, 2, 1, 0, 0, "Coding")]
    [InlineData(0, 0, 2, 0, 0, 1, 0, "Planning")]
    [InlineData(0, 0, 0, 0, 0, 0, 0, "General")]
    public void SessionAnalysisEngine_ClassifyCategory_ReturnsExpectedCategory(
        int testHits, int debugHits, int planHits, int codingHits,
        int editTools, int inspectTools, int bashTools,
        string expectedCategory)
    {
        string category = SessionAnalysisEngine.ClassifyCategory(
            testHits, debugHits, planHits, codingHits,
            editTools, inspectTools, bashTools);

        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void SessionAnalysisEngine_ParsesTranscript_ComputesOneShotRateAndCategory()
    {
        using var temp = new TempDirectory();
        string projectsDir = Path.Combine(temp.Path, "projects", "p1");
        Directory.CreateDirectory(projectsDir);

        string transcriptFile = Path.Combine(projectsDir, "session_1.jsonl");

        // Write sample session with 3 edits: fileA (1st edit), fileA (retry edit), fileB (1st edit) -> 2 one-shot out of 3 edits = 66.7%
        File.WriteAllLines(transcriptFile, [
            """{"type":"user","message":{"content":"Please fix the bug in main.cs"}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Edit","input":{"file_path":"C:\\src\\main.cs"}}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Edit","input":{"file_path":"C:\\src\\main.cs"}}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Edit","input":{"file_path":"C:\\src\\helper.cs"}}]}}"""
        ]);

        string dbPath = Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var engine = new SessionAnalysisEngine(db, claudeProjectsRoot: temp.Path, userProfileDir: temp.Path);

        var sessions = engine.AnalyzeClaudeSessions();

        Assert.Single(sessions);
        var session = sessions[0];
        Assert.Equal("Debugging", session.Category);
        Assert.Equal(3, session.TotalEdits);
        Assert.Equal(1, session.RetriedEdits);

        var report = engine.GenerateReport();
        Assert.Equal(66.7, report.OneShotMetrics.SuccessRatePercentage, 1);
        Assert.Equal(3, report.OneShotMetrics.TotalEdits);
        Assert.Equal(1, report.OneShotMetrics.RetryEdits);
        Assert.Equal(2, report.OneShotMetrics.OneShotEdits);
    }

    [Fact]
    public void SessionAnalysisEngine_DetectsReReadWasteSignal()
    {
        using var temp = new TempDirectory();
        string projectsDir = Path.Combine(temp.Path, "projects", "p1");
        Directory.CreateDirectory(projectsDir);

        string transcriptFile = Path.Combine(projectsDir, "session_2.jsonl");

        // Write transcript with fileA read 4 times (> 2 times -> 2 re-reads)
        File.WriteAllLines(transcriptFile, [
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"View","input":{"file_path":"C:\\src\\main.cs"}}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"View","input":{"file_path":"C:\\src\\main.cs"}}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"View","input":{"file_path":"C:\\src\\main.cs"}}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"View","input":{"file_path":"C:\\src\\main.cs"}}]}}"""
        ]);

        string dbPath = Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var engine = new SessionAnalysisEngine(db, claudeProjectsRoot: temp.Path, userProfileDir: temp.Path);
        var summary = engine.ParseSingleTranscript(transcriptFile);

        Assert.NotNull(summary);
        Assert.Equal(2, summary.ReReadCount);
    }

    [Fact]
    public void SessionAnalysisEngine_ComputesToolEfficiencyRows_IncludesCanonicalTools()
    {
        using var temp = new TempDirectory();
        string dbPath = Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        db.UpsertDailyAggregate("Claude Code", today, 10000, 5000, 0.50, 2);
        db.UpsertDailyAggregate("Hermes Agent", today, 20000, 10000, 1.20, 4);

        var engine = new SessionAnalysisEngine(db, claudeProjectsRoot: temp.Path, userProfileDir: temp.Path);
        var report = engine.GenerateReport();

        Assert.Equal(5, report.ToolEfficiencies.Count);

        var claudeRow = report.ToolEfficiencies.First(t => t.ToolName == "Claude Code");
        Assert.Equal(2, claudeRow.SessionCount);
        Assert.Equal(15000, claudeRow.TotalTokens);
        Assert.Equal(7500, claudeRow.TokensPerSession);
        Assert.Equal(0.50, claudeRow.TotalCostUsd);
        Assert.Equal(0.25, claudeRow.CostPerSession);

        var openCodeRow = report.ToolEfficiencies.First(t => t.ToolName == "OpenCode");
        Assert.Equal(0, openCodeRow.SessionCount);
        Assert.Equal(0, openCodeRow.TotalTokens);
    }

    [Fact]
    public void SessionAnalysisEngine_ComputeHealthGrade_AssignsGradeAndChecks()
    {
        using var temp = new TempDirectory();

        // Create a small valid CLAUDE.md
        string claudeDir = Path.Combine(temp.Path, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, "CLAUDE.md"), "# Project Rules\nKeep it simple.");

        string dbPath = Path.Combine(temp.Path, "history.db");
        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var engine = new SessionAnalysisEngine(db, claudeProjectsRoot: temp.Path, userProfileDir: temp.Path);

        // Grade with high one-shot rate and 0 re-reads
        var gradeA = engine.ComputeHealthGrade(90.0, 2);
        Assert.Equal("A", gradeA.Grade);
        Assert.Equal(100, gradeA.Score);
        Assert.Equal(4, gradeA.Checks.Count);

        // Grade with low one-shot rate and high re-reads
        var gradeD = engine.ComputeHealthGrade(50.0, 10);
        Assert.True(gradeD.Score < 80);
        Assert.True(gradeD.Grade == "C" || gradeD.Grade == "D" || gradeD.Grade == "F");
    }

    [Fact]
    public void SessionAnalysisEngine_RealMachineData_GeneratesReport()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dbPath = Path.Combine(userProfile, "AppData", "Local", "AiToolsMonitor", "history.db");

        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var ingester = new UsageHistoryIngester(db);
        ingester.Ingest(DateTimeOffset.UtcNow);

        var engine = new SessionAnalysisEngine(db);
        var report = engine.GenerateReport();

        Assert.NotNull(report);
        Assert.NotNull(report.HealthGrade);

        System.Diagnostics.Debug.WriteLine($"[REAL DATA] Health Grade: {report.HealthGrade.Grade} (Score: {report.HealthGrade.Score})");
        System.Diagnostics.Debug.WriteLine($"[REAL DATA] One-Shot Success Rate: {report.OneShotMetrics.SuccessRatePercentage}% ({report.OneShotMetrics.OneShotEdits}/{report.OneShotMetrics.TotalEdits} edits)");
        System.Diagnostics.Debug.WriteLine($"[REAL DATA] Evaluated Sessions: {report.OneShotMetrics.EvaluatedSessionsCount}");

        foreach (var cat in report.Categories)
        {
            System.Diagnostics.Debug.WriteLine($"[REAL DATA] Category: {cat.Category} -> {cat.SessionCount} sessions ({cat.Percentage:0.0}%)");
        }
    }

    [Fact]
    public void AnalysisForm_RendersRealDataScreenshot()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dbPath = Path.Combine(userProfile, "AppData", "Local", "AiToolsMonitor", "history.db");

        using var db = new HistoryDatabase(dbPath);
        db.EnsureCreated();

        var ingester = new UsageHistoryIngester(db);
        ingester.Ingest(DateTimeOffset.UtcNow);

        var engine = new SessionAnalysisEngine(db);

        using var form = new AnalysisForm(engine);
        form.CreateControl();
        form.Show();
        System.Windows.Forms.Application.DoEvents();

        using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));

        string artifactDir = @"C:\Users\shubh\.gemini\antigravity-cli\brain\80712477-8da1-4db8-9cc8-b3e685e06d00";
        if (!Directory.Exists(artifactDir)) Directory.CreateDirectory(artifactDir);

        string outputPath = Path.Combine(artifactDir, "analysis_screenshot.png");
        bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);

        Assert.True(File.Exists(outputPath));
        form.Close();
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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path, recursive: true);
        }
    }
}
