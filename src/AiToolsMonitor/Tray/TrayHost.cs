using System.Globalization;
using System.Text;
using AiToolsMonitor.Export;
using AiToolsMonitor.Monitoring;
using AiToolsMonitor.Popup;
using AiToolsMonitor.History;
using AiToolsMonitor.Projects;

namespace AiToolsMonitor.Tray;

/// <summary>
/// Owns the NotifyIcon and wires the click behavior specified in PRD FR-4:
/// left-click toggles the popup (deliberate deviation from pystray's
/// right-click-only default -- that convention mismatch was the root cause
/// of the prototype "not working" complaint), right-click shows the menu.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly StatusPopup _popup = new();
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly ProcessEnumerator _enumerator = new();
    private readonly BudgetThresholdTracker _budgetThresholdTracker = new();
    private StatusSnapshot? _currentSnapshot;
    private int _runningCount = -1;
    private bool _firstRunNoticeShown;
    private readonly HistoryDatabase _historyDb = new();
    private UsageHistoryIngester _ingester = null!;

    public TrayHost()
    {
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

        _historyDb.EnsureCreated();
        _ingester = new UsageHistoryIngester(_historyDb);

        Poll(); // first sample immediately so the popup isn't empty on first open
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (_popup.Visible) _popup.Hide();
            else _popup.ShowNearTray();
        }
        // Right-click is handled natively by ContextMenuStrip assignment above.
    }

    private async void Poll()
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
            string percent = tool.Quota!.PrimaryPercent!.Value.ToString(
                "0.#",
                CultureInfo.InvariantCulture);
            _notifyIcon.ShowBalloonTip(
                4000,
                "AI Tools Monitor budget warning",
                $"{tool.DisplayName} primary usage has reached {percent}%.",
                ToolTipIcon.Warning);
        }

    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => _popup.ShowNearTray());
        menu.Items.Add("Refresh now", null, (_, _) => Poll());
        var recentProjects = new ToolStripMenuItem("Recent Projects");
        recentProjects.DropDownOpening += (_, _) => PopulateRecentProjects(recentProjects);
        PopulateRecentProjects(recentProjects);
        menu.Items.Add(recentProjects);
        menu.Items.Add("Export usage data...", null, (_, _) => ExportUsageData());
        menu.Items.Add("Open taskbar settings", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true }); }
            catch { /* best effort */ }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        return menu;
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

        if (dialog.ShowDialog(_popup) != DialogResult.OK)
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
        _historyDb.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _popup.Dispose();
    }
}
