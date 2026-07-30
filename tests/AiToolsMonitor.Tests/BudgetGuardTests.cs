using AiToolsMonitor.Budget;
using AiToolsMonitor.History;

namespace AiToolsMonitor.Tests;

public class BudgetConfigTests
{
    [Fact]
    public void Load_ReturnsDefaults_WhenFileDoesNotExist()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_budget_{Guid.NewGuid():N}.json");
        try
        {
            var config = BudgetConfig.Load(path);
            Assert.Equal(5.0, config.SoftCapUsd);
            Assert.Equal(15.0, config.HardCapUsd);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Save_And_Load_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_budget_{Guid.NewGuid():N}.json");
        try
        {
            var original = new BudgetConfig { SoftCapUsd = 3.0, HardCapUsd = 10.0 };
            original.Save(path);

            var loaded = BudgetConfig.Load(path);
            Assert.Equal(3.0, loaded.SoftCapUsd);
            Assert.Equal(10.0, loaded.HardCapUsd);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileIsCorrupt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_budget_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not valid json {{{");
            var config = BudgetConfig.Load(path);
            Assert.Equal(5.0, config.SoftCapUsd);
            Assert.Equal(15.0, config.HardCapUsd);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}

public class BudgetGuardTests : IDisposable
{
    private readonly string _dbPath;
    private readonly HistoryDatabase _db;

    public BudgetGuardTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_budget_{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_dbPath);
        _db.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void CheckToday_ReturnsNone_WhenCostBelowSoftCap()
    {
        var config = new BudgetConfig { SoftCapUsd = 5.0, HardCapUsd = 15.0 };
        var guard = new BudgetGuard(config, _db);

        // No data in DB, so cost is 0
        Assert.Equal(BudgetAlertLevel.None, guard.CheckToday());
    }

    [Fact]
    public void CheckToday_ReturnsSoftCapExceeded_WhenCostExceedsSoftCap()
    {
        var config = new BudgetConfig { SoftCapUsd = 5.0, HardCapUsd = 15.0 };
        var guard = new BudgetGuard(config, _db);

        // Insert enough cost to exceed soft cap
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _db.UpsertDailyAggregate("TestTool", today, 0, 0, 6.0, 1);

        Assert.Equal(BudgetAlertLevel.SoftCapExceeded, guard.CheckToday());
    }

    [Fact]
    public void CheckToday_ReturnsHardCapExceeded_WhenCostExceedsHardCap()
    {
        var config = new BudgetConfig { SoftCapUsd = 5.0, HardCapUsd = 15.0 };
        var guard = new BudgetGuard(config, _db);

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _db.UpsertDailyAggregate("TestTool", today, 0, 0, 16.0, 1);

        Assert.Equal(BudgetAlertLevel.HardCapExceeded, guard.CheckToday());
    }

    [Fact]
    public void CheckToday_ReturnsNone_AfterFirstNotification_WhenCostStillExceeds()
    {
        var config = new BudgetConfig { SoftCapUsd = 5.0, HardCapUsd = 15.0 };
        var guard = new BudgetGuard(config, _db);

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _db.UpsertDailyAggregate("TestTool", today, 0, 0, 6.0, 1);

        // First call returns alert
        Assert.Equal(BudgetAlertLevel.SoftCapExceeded, guard.CheckToday());
        // Second call returns None (already notified today)
        Assert.Equal(BudgetAlertLevel.None, guard.CheckToday());
    }

    [Fact]
    public void CheckToday_PrioritizesHardCapOverSoftCap()
    {
        var config = new BudgetConfig { SoftCapUsd = 5.0, HardCapUsd = 10.0 };
        var guard = new BudgetGuard(config, _db);

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _db.UpsertDailyAggregate("TestTool", today, 0, 0, 12.0, 1);

        // Hard cap exceeded (12 > 10) takes priority over soft cap
        Assert.Equal(BudgetAlertLevel.HardCapExceeded, guard.CheckToday());
    }

    [Fact]
    public void ResetTodayNotifications_AllowsReNotification()
    {
        var config = new BudgetConfig { SoftCapUsd = 5.0, HardCapUsd = 15.0 };
        var guard = new BudgetGuard(config, _db);

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _db.UpsertDailyAggregate("TestTool", today, 0, 0, 6.0, 1);

        Assert.Equal(BudgetAlertLevel.SoftCapExceeded, guard.CheckToday());
        Assert.Equal(BudgetAlertLevel.None, guard.CheckToday());

        guard.ResetTodayNotifications();

        // After reset, should notify again
        Assert.Equal(BudgetAlertLevel.SoftCapExceeded, guard.CheckToday());
    }

    [Fact]
    public void CheckToday_UsesConfigValues_NotDefaults()
    {
        var config = new BudgetConfig { SoftCapUsd = 1.0, HardCapUsd = 2.0 };
        var guard = new BudgetGuard(config, _db);

        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _db.UpsertDailyAggregate("TestTool", today, 0, 0, 1.5, 1);

        // 1.5 > 1.0 soft cap, but < 2.0 hard cap
        Assert.Equal(BudgetAlertLevel.SoftCapExceeded, guard.CheckToday());
    }
}

public class CostAnomalyDetectorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly HistoryDatabase _db;

    public CostAnomalyDetectorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_anomaly_{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_dbPath);
        _db.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void Detect_ReturnsFalse_WhenInsufficientHistory()
    {
        var detector = new CostAnomalyDetector(_db);
        var (isAnomaly, todayCost, mean, stddev, zScore) = detector.Detect();

        Assert.False(isAnomaly);
        Assert.Equal(0, todayCost);
        Assert.Equal(0, mean);
    }

    [Fact]
    public void Detect_ReturnsFalse_WhenTodayCostIsNormal()
    {
        var detector = new CostAnomalyDetector(_db);
        var today = DateTime.UtcNow.Date;

        // Insert 10 days of consistent cost ($1/day)
        for (int i = 1; i <= 10; i++)
        {
            var date = today.AddDays(-i).ToString("yyyy-MM-dd");
            _db.UpsertDailyAggregate("TestTool", date, 0, 0, 1.0, 1);
        }

        // Today is also $1 — normal
        _db.UpsertDailyAggregate("TestTool", today.ToString("yyyy-MM-dd"), 0, 0, 1.0, 1);

        var (isAnomaly, todayCost, mean, stddev, zScore) = detector.Detect();

        Assert.False(isAnomaly);
        Assert.Equal(1.0, todayCost, 2);
        Assert.True(stddev < 0.01); // zero stddev since all values identical
    }

    [Fact]
    public void Detect_ReturnsTrue_WhenTodayCostIsAnomalouslyHigh()
    {
        var detector = new CostAnomalyDetector(_db);
        var today = DateTime.UtcNow.Date;

        // Insert 10 days with varying costs around $1-2
        double[] costs = [0.8, 1.2, 0.9, 1.1, 1.0, 0.7, 1.3, 0.95, 1.05, 1.15];
        for (int i = 0; i < costs.Length; i++)
        {
            var date = today.AddDays(-(i + 1)).ToString("yyyy-MM-dd");
            _db.UpsertDailyAggregate("TestTool", date, 0, 0, costs[i], 1);
        }

        // Today is $10 — way above mean
        _db.UpsertDailyAggregate("TestTool", today.ToString("yyyy-MM-dd"), 0, 0, 10.0, 1);

        var (isAnomaly, todayCost, mean, stddev, zScore) = detector.Detect();

        Assert.True(isAnomaly);
        Assert.Equal(10.0, todayCost, 2);
        Assert.True(mean > 0.9 && mean < 1.1);
        Assert.True(zScore > 2.0);
    }

    [Fact]
    public void Detect_ComputesCorrectZScore()
    {
        var detector = new CostAnomalyDetector(_db);
        var today = DateTime.UtcNow.Date;

        // Create known distribution: [1, 2, 3, 4, 5] → mean=3, stddev≈1.414
        double[] costs = [1.0, 2.0, 3.0, 4.0, 5.0];
        for (int i = 0; i < costs.Length; i++)
        {
            var date = today.AddDays(-(i + 1)).ToString("yyyy-MM-dd");
            _db.UpsertDailyAggregate("TestTool", date, 0, 0, costs[i], 1);
        }

        // Today = 6.0 → z = (6 - 3) / 1.414 ≈ 2.12
        _db.UpsertDailyAggregate("TestTool", today.ToString("yyyy-MM-dd"), 0, 0, 6.0, 1);

        var (isAnomaly, todayCost, mean, stddev, zScore) = detector.Detect();

        Assert.True(isAnomaly);
        Assert.Equal(3.0, mean, 2);
        Assert.True(stddev > 1.4 && stddev < 1.5);
        Assert.True(zScore > 2.1 && zScore < 2.2);
    }

    [Fact]
    public void Detect_ReturnsFalse_WhenTodayCostIsZero()
    {
        var detector = new CostAnomalyDetector(_db);
        var today = DateTime.UtcNow.Date;

        // Historical data exists
        for (int i = 1; i <= 5; i++)
        {
            var date = today.AddDays(-i).ToString("yyyy-MM-dd");
            _db.UpsertDailyAggregate("TestTool", date, 0, 0, 1.0, 1);
        }

        // Today has no cost (0)
        var (isAnomaly, todayCost, mean, stddev, zScore) = detector.Detect();

        Assert.False(isAnomaly);
        Assert.Equal(0, todayCost);
    }

    [Fact]
    public void Detect_IgnoresZeroCostDays_InHistory()
    {
        var detector = new CostAnomalyDetector(_db);
        var today = DateTime.UtcNow.Date;

        // Insert mix of zero and non-zero days
        for (int i = 1; i <= 10; i++)
        {
            var date = today.AddDays(-i).ToString("yyyy-MM-dd");
            double cost = i % 3 == 0 ? 0.0 : 1.0; // some zero, some $1
            _db.UpsertDailyAggregate("TestTool", date, 0, 0, cost, 1);
        }

        // Today is $1 — normal
        _db.UpsertDailyAggregate("TestTool", today.ToString("yyyy-MM-dd"), 0, 0, 1.0, 1);

        var (isAnomaly, todayCost, mean, stddev, zScore) = detector.Detect();

        Assert.False(isAnomaly);
        // Mean should be 1.0 (only non-zero days counted)
        Assert.Equal(1.0, mean, 2);
    }
}