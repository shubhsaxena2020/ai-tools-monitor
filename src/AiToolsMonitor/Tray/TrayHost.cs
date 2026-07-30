using System.Globalization;
using System.Text;
using AiToolsMonitor.Budget;
using AiToolsMonitor.Export;
using AiToolsMonitor.Monitoring;
using AiToolsMonitor.Popup;
using AiToolsMonitor.History;
using AiToolsMonitor.Projects;
using AiToolsMonitor.Shell;

namespace AiToolsMonitor.Tray;

/// <summary>
/// Owns the NotifyIcon and wires the click behavior specified in PRD FR-4:
/// left-click opens or focuses the main shell (deliberate deviation from pystray's
/// right-click-only default -- that convention mismatch was the root cause
/// of the prototype "not working" complaint), right-click shows the menu.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly StatusPopup _popup;
    private readonly MainShellForm _mainShell;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly ProcessEnumerator _enumerator = new();
    private readonly BudgetThresholdTracker _budgetThresholdTracker = new();
    private StatusSnapshot? _currentSnapshot;
    private int _runningCount = -1;
    private bool _firstRunNoticeShown;
    private readonly HistoryDatabase _historyDb = new();
    private readonly UsageHistoryIngester _ingester;
    private readonly BudgetConfig _budgetConfig;
    private readonly BudgetGuard _budgetGuard;
    private readonly CostAnomalyDetector _anomalyDetector;

    public TrayHost()
    {
        _historyDb.EnsureCreated();
        _ingester = new UsageHistoryIngester(_historyDb);
        _budgetConfig = BudgetConfig.Load();
        _budgetGuard = new BudgetGuard(_budgetConfig, _historyDb);
        _anomalyDetector = new CostAnomalyDetector(_historyDb);

        _popup = new StatusPopup(embedded: true);
        _mainShell = new MainShellForm(
            _popup,
            _historyDb,
            _budgetConfig,
            _anomalyDetector,
            () => _budgetGuard.ResetTodayNotifications());

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.Render(0),
            Text = "AI Tools Monitor: idle",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };

        // MouseUp (not Click) so we can distinguish left vs right ourselves --
        // NotifyIcon.Click fires for both buttons and only the left-click
        // default action is easily separable via MouseUp's button arg.
        _notifyIcon.MouseUp += OnTrayMouseUp;

        _pollTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _pollTimer.Tick += (_, _) => Poll();
        _pollTimer.Start();

        Poll(); // first sample immediately so the dashboard isn't empty on first open
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _mainShell.ShowPage(ShellPage.Dashboard);
        }
        // Right-click is handled natively by ContextMenuStrip assignment above.
    }

    private async void Poll()
    {
        try
        {
            var samples = _enumerator.Scan();
            var rawSnapshot = ToolDetector.Aggregate(samples);

            var codexTask = CodexQuotaClient.GetQuotaAsync();
            var claudeQuota = ClaudeCodeQuotaReader.GetQuota();
            var hermesQuota = HermesQuotaClient.GetQuota();
            var openCodeQuota = OpenCodeQuotaClient.GetQuota();
            var codexQuota = await codexTask;

            var enrichedTools = rawSnapshot.Tools.Select(t =>
            {
                if (t.DisplayName == "Codex") return t with { Quota = codexQuota };
                if (t.DisplayName == "Claude Code") return t with { Quota = claudeQuota };
                if (t.DisplayName == "Hermes Agent") return t with { Quota = hermesQuota };
                if (t.DisplayName == "OpenCode") return t with { Quota = openCodeQuota };
                return t;
            }).ToList();

            var snapshot = new StatusSnapshot(enrichedTools, rawSnapshot.SampledAtUtc);
            _currentSnapshot = snapshot;
            _popup.Render(snapshot);

            try
            {
                _ingester.Ingest();
                var (tokens, cost) = _historyDb.GetTodaySummary();
                _popup.UpdateTodaySummary(tokens, cost);
            }
            catch { /* best effort — history ingestion must never crash the app */ }

            // Budget guard check
            try
            {
                var alertLevel = _budgetGuard.CheckToday();
                if (alertLevel == BudgetAlertLevel.HardCapExceeded)
                {
                    _notifyIcon.ShowBalloonTip(
                        6000,
                        "AI Tools Monitor — HARD CAP EXCEEDED",
                        $"Today's cost has exceeded the hard cap of ${_budgetConfig.HardCapUsd:F2}.",
                        ToolTipIcon.Error);
                }
                else if (alertLevel == BudgetAlertLevel.SoftCapExceeded)
                {
                    _notifyIcon.ShowBalloonTip(
                        4000,
                        "AI Tools Monitor — soft cap warning",
                        $"Today's cost has exceeded the soft cap of ${_budgetConfig.SoftCapUsd:F2}.",
                        ToolTipIcon.Warning);
                }
            }
            catch { /* best effort */ }

            if (snapshot.RunningCount != _runningCount)
            {
                _runningCount = snapshot.RunningCount;
                _notifyIcon.Icon = TrayIconRenderer.Render(_runningCount);
                _notifyIcon.Text = _runningCount == 0
                    ? "AI Tools Monitor: idle"
                    : $"AI Tools Monitor: {_runningCount} running";
            }

            if (!_firstRunNoticeShown)
            {
                _firstRunNoticeShown = true;
                _notifyIcon.ShowBalloonTip(4000, "AI Tools Monitor",
                    "Running. If you don't see the icon, open the hidden icons arrow and drag it beside the clock.",
                    ToolTipIcon.Info);
            }

            foreach (var tool in _budgetThresholdTracker.GetNewlyExceededTools(snapshot))
            {
                if (tool.Quota?.PrimaryPercent is { } primaryPercent)
                {
                    string percent = primaryPercent.ToString("0.#", CultureInfo.InvariantCulture);
                    try
                    {
                        _notifyIcon.ShowBalloonTip(
                            4000,
                            "AI Tools Monitor budget warning",
                            $"{tool.DisplayName} primary usage has reached {percent}%.",
                            ToolTipIcon.Warning);
                    }
                    catch { /* best effort */ }
                }
            }
        }
        catch
        {
            // Best effort - polling must never crash the process
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => _mainShell.ShowPage(ShellPage.Dashboard));
        menu.Items.Add("Refresh now", null, (_, _) => Poll());
        var recentProjects = new ToolStripMenuItem("Recent Projects");
        recentProjects.DropDownOpening += (_, _) => PopulateRecentProjects(recentProjects);
        PopulateRecentProjects(recentProjects);
        menu.Items.Add(recentProjects);

        // Quick launch submenu
        var quickLaunch = new ToolStripMenuItem("Quick launch");
        string[] toolCommands = ["claude", "codex", "opencode", "hermes", "agy"];
        foreach (var cmd in toolCommands)
        {
            quickLaunch.DropDownItems.Add(cmd, null, (_, _) => LaunchTool(cmd));
        }
        menu.Items.Add(quickLaunch);

        menu.Items.Add("Edit budget...", null, (_, _) => ShowBudgetEditForm());
        menu.Items.Add("Export usage data...", null, (_, _) => ExportUsageData());
        menu.Items.Add("Analysis...", null, (_, _) => ShowAnalysisForm());
        menu.Items.Add("Cost report...", null, (_, _) => ShowCostReport());
        menu.Items.Add("Usage history...", null, (_, _) => OpenUsageHistory());
        menu.Items.Add("Open taskbar settings", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true }); }
            catch { /* best effort */ }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        return menu;
    }

    private void ShowAnalysisForm()
    {
        try
        {
            _mainShell.ShowPage(ShellPage.Analysis);
        }
        catch
        {
            // Best effort
        }
    }

    private void ShowBudgetEditForm()
    {
        try
        {
            _mainShell.ShowPage(ShellPage.Budget);
        }
        catch
        {
            // Best effort
        }
    }

    private static void LaunchTool(string command)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {command}",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best effort.
        }
    }

    private void ShowCostReport()
    {
        try
        {
            _mainShell.ShowPage(ShellPage.CostReport);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void PopulateRecentProjects(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        var projects = RecentProjectProvider.GetRecentProjects();
        if (projects.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("No recent projects") { Enabled = false });
            return;
        }

        foreach (string project in projects)
        {
            menu.DropDownItems.Add(project, null, (_, _) => OpenProject(project));
        }
    }

    private static void OpenProject(string project)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k cd /d \"{project}\"",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best effort.
        }
    }

    private void OpenUsageHistory()
    {
        try
        {
            _mainShell.ShowPage(ShellPage.UsageHistory);
        }
        catch
        {
            // Best effort — never crash the app
        }
    }

    private void ExportUsageData()
    {
        StatusSnapshot? snapshot = _currentSnapshot;
        if (snapshot is null)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Export usage data",
            Filter = "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json",
            DefaultExt = "csv",
            AddExtension = true,
            FileName = $"ai-tools-usage-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
        };

        DialogResult result = _mainShell.Visible
            ? dialog.ShowDialog(_mainShell)
            : dialog.ShowDialog();
        if (result != DialogResult.OK)
            return;

        try
        {
            string content = Path.GetExtension(dialog.FileName)
                .Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? StatusSnapshotFormatter.ToJson(snapshot)
                : StatusSnapshotFormatter.ToCsv(snapshot);
            File.WriteAllText(
                dialog.FileName,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Best effort.
        }
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        _mainShell.Dispose();
        _historyDb.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
