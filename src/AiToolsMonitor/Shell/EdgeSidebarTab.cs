using System.Drawing.Drawing2D;
using Microsoft.Win32;
using AiToolsMonitor.Monitoring;

namespace AiToolsMonitor.Shell;

/// <summary>
/// Always-on-top edge-docked peek sidebar: a small tab on the right screen edge
/// that slides out a full tool-status panel on click, retracts on focus loss.
///
/// Interaction design (from EDGE_SIDEBAR_RESEARCH.md):
///   - Click-triggered (avoids accidental hover activation on desktop).
///   - 200ms ease-out slide-out, 150ms ease-in retract.
///   - Retracts on Deactivate, mouse-leave (400ms grace), or Escape.
///   - Tab shows active-tools count dot when tools are running.
/// </summary>
public sealed class EdgeSidebarTab : Form
{
    // -- Layout constants --
    private const int TabWidth = 28;
    private const int TabHoverWidth = 32;
    private const int TabHeight = 120;
    private const int PanelWidth = 350;
    private const int ChevronSize = 10;

    // -- Animation timing --
    private const int ExpandDurationMs = 200;
    private const int CollapseDurationMs = 150;
    private const int TimerIntervalMs = 16; // ~60 fps
    private const int MouseLeaveGraceMs = 400;

    // -- Win32 constants --
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;

    private enum SidebarState { Collapsed, Hovered, SlidingOut, Expanded, SlidingIn }

    private SidebarState _state = SidebarState.Collapsed;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly System.Windows.Forms.Timer _mouseLeaveTimer;
    private double _animProgress; // 0 = collapsed, 1 = expanded
    private DateTime _animStart;
    private int _animDurationMs;
    private bool _isHovered;

    // -- Content controls (populated when expanded) --
    private readonly Panel _panelContent;
    private readonly FlowLayoutPanel _cardContainer;
    private readonly Label _lastUpdatedLabel;
    private readonly Label _runningCountBadge;
    private readonly Label _todaySummaryLabel;

    // -- Theme --
    private ThemeSettings _theme;
    private Color _surfaceColor;
    private Color _primaryText;
    private Color _secondaryText;
    private Color _pinkAccent;
    private Color _tabBackground;
    private Color _tabBorder;

    // -- Snapshot data --
    private StatusSnapshot? _snapshot;

    public EdgeSidebarTab()
    {
        _theme = ReadThemeSettings();

        // Form setup: borderless, always-on-top, no taskbar entry
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Text = "AI Tools Monitor Edge Sidebar";
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;

        // Start collapsed at right edge, vertically centered
        var screen = Screen.PrimaryScreen!.WorkingArea;
        int collapsedX = screen.Right - TabWidth;
        int collapsedY = screen.Top + (screen.Height - TabHeight) / 2;
        Location = new Point(collapsedX, collapsedY);
        Size = new Size(TabWidth, TabHeight);

        DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        // -- Build the expandable panel content --
        _cardContainer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 4, 12, 4),
            BackColor = Color.Transparent,
            Visible = false,
        };

        _lastUpdatedLabel = new Label
        {
            Text = "Last updated: --:--:--",
            Font = new Font("Segoe UI", 8f),
            Dock = DockStyle.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Visible = false,
        };

        _runningCountBadge = new Label
        {
            Text = "0 Active",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Dock = DockStyle.Right,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
            Visible = false,
        };

        _todaySummaryLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 24),
            BackColor = Color.Transparent,
            Visible = false,
        };

        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(16, 6, 16, 6),
            BackColor = Color.Transparent,
            Visible = false,
        };
        footerPanel.Controls.Add(_lastUpdatedLabel);
        footerPanel.Controls.Add(_runningCountBadge);
        footerPanel.Controls.Add(_todaySummaryLabel);

        _panelContent = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Visible = false,
        };
        _panelContent.Controls.Add(_cardContainer);
        _panelContent.Controls.Add(footerPanel);

        Controls.Add(_panelContent);

        // -- Animation timer --
        _animationTimer = new System.Windows.Forms.Timer { Interval = TimerIntervalMs };
        _animationTimer.Tick += OnAnimationTick;

        // -- Mouse-leave grace timer --
        _mouseLeaveTimer = new System.Windows.Forms.Timer { Interval = MouseLeaveGraceMs };
        _mouseLeaveTimer.Tick += OnMouseLeaveTimeout;

        // -- Events --
        Deactivate += (_, _) => Collapse();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Collapse();
        };

        ApplyThemeColors();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExTopmost | WsExNoActivate;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Let normal processing happen but mark entire client as HTCLIENT
        // so clicks register without activating other windows.
        if (m.Msg == WmNcHitTest)
        {
            m.Result = (nint)HtClient;
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// Feed a new status snapshot from TrayHost's Poll() cycle.
    /// </summary>
    public void Render(StatusSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (_state == SidebarState.Expanded || _state == SidebarState.SlidingOut)
        {
            PopulatePanel(snapshot);
        }
        Invalidate(); // repaint tab (may update status dot)
    }

    /// <summary>
    /// Update the "today" summary line shown in the footer.
    /// </summary>
    public void UpdateTodaySummary(long totalTokens, double totalCost)
    {
        if (totalTokens > 0 || totalCost > 0)
        {
            string tokenStr = FormatTokenCount(totalTokens);
            _todaySummaryLabel.Text = $"Today: {tokenStr} tokens, ${totalCost:0.00}";
            _todaySummaryLabel.Visible = true;
        }
        else
        {
            _todaySummaryLabel.Visible = false;
        }
    }

    // ──────────────────────── Tab click / hover ────────────────────────

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        _mouseLeaveTimer.Stop();

        if (_state == SidebarState.Collapsed)
        {
            _state = SidebarState.Hovered;
        }
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;

        if (_state == SidebarState.Hovered)
        {
            _state = SidebarState.Collapsed;
            Invalidate();
        }
        else if (_state == SidebarState.Expanded)
        {
            // Start grace timer — if mouse doesn't return, collapse
            _mouseLeaveTimer.Stop();
            _mouseLeaveTimer.Start();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            if (_state is SidebarState.Collapsed or SidebarState.Hovered)
                Expand();
            else if (_state == SidebarState.Expanded)
                Collapse();
        }
    }

    private void OnMouseLeaveTimeout(object? sender, EventArgs e)
    {
        _mouseLeaveTimer.Stop();
        if (_state == SidebarState.Expanded && !_isHovered)
        {
            Collapse();
        }
    }

    // ──────────────────────── Expand / Collapse ────────────────────────

    private void Expand()
    {
        if (_state is SidebarState.SlidingOut or SidebarState.Expanded)
            return;

        _state = SidebarState.SlidingOut;
        ShowPanelContent(true);

        if (_snapshot is { } snap)
            PopulatePanel(snap);

        StartAnimation(from: 0, to: 1, durationMs: ExpandDurationMs);
    }

    private void Collapse()
    {
        if (_state is SidebarState.Collapsed or SidebarState.SlidingIn)
            return;

        _state = SidebarState.SlidingIn;
        _mouseLeaveTimer.Stop();
        StartAnimation(from: 1, to: 0, durationMs: CollapseDurationMs);
    }

    private void StartAnimation(double from, double to, int durationMs)
    {
        _animProgress = from;
        _animStart = DateTime.UtcNow;
        _animDurationMs = durationMs;
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _animStart).TotalMilliseconds;
        double t = Math.Min(1.0, elapsed / _animDurationMs);

        // Ease-out for expand, ease-in for collapse
        double eased = _state == SidebarState.SlidingOut
            ? 1.0 - Math.Pow(1.0 - t, 3)    // cubic ease-out
            : t * t * t;                       // cubic ease-in

        double from = _state == SidebarState.SlidingOut ? 0 : 1;
        double to = _state == SidebarState.SlidingOut ? 1 : 0;
        _animProgress = from + (to - from) * eased;

        ApplyAnimationFrame();

        if (t >= 1.0)
        {
            _animationTimer.Stop();
            _animProgress = to;

            if (_state == SidebarState.SlidingOut)
            {
                _state = SidebarState.Expanded;
            }
            else // SlidingIn
            {
                _state = SidebarState.Collapsed;
                ShowPanelContent(false);
            }

            ApplyAnimationFrame();
        }
    }

    private void ApplyAnimationFrame()
    {
        var screen = Screen.PrimaryScreen!.WorkingArea;

        // Interpolate width: TabWidth → PanelWidth
        int width = (int)(TabWidth + (PanelWidth - TabWidth) * _animProgress);

        // Interpolate height: TabHeight → full working area height
        int height = (int)(TabHeight + (screen.Height - TabHeight) * _animProgress);

        // Interpolate Y position: centered → top of working area
        int collapsedY = screen.Top + (screen.Height - TabHeight) / 2;
        int expandedY = screen.Top;
        int y = (int)(collapsedY + (expandedY - collapsedY) * _animProgress);

        // Right edge stays pinned to screen right
        int x = screen.Right - width;

        Location = new Point(x, y);
        Size = new Size(width, height);
    }

    private void ShowPanelContent(bool visible)
    {
        _panelContent.Visible = visible;
        _cardContainer.Visible = visible;
        _lastUpdatedLabel.Visible = visible;
        _runningCountBadge.Visible = visible;
        // _todaySummaryLabel visibility is managed by UpdateTodaySummary
    }

    // ──────────────────────── Panel content ────────────────────────

    private void PopulatePanel(StatusSnapshot snapshot)
    {
        _cardContainer.SuspendLayout();
        _cardContainer.Controls.Clear();

        int cardWidth = PanelWidth - 48; // 350 - outer padding - inner padding

        foreach (var tool in snapshot.Tools)
        {
            var card = CreateToolCard(tool, cardWidth);
            _cardContainer.Controls.Add(card);
        }

        _lastUpdatedLabel.Text = $"Updated {snapshot.SampledAtUtc.ToLocalTime():HH:mm:ss}";
        _runningCountBadge.Text = $"{snapshot.RunningCount} Running";

        _cardContainer.ResumeLayout(true);
    }

    private Panel CreateToolCard(ToolStatus tool, int cardWidth)
    {
        bool supportsQuota = tool.Quota is not null;
        int cardHeight = supportsQuota ? 110 : 48;

        var card = new GlassSidePanel(cardWidth, cardHeight, _surfaceColor, _pinkAccent, _theme.IsDark)
        {
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(10, 8, 10, 8),
        };

        // Tool name + status + metrics row
        string stateText = tool.State.ToString();
        Color stateColor = tool.State switch
        {
            ToolState.Active => Color.FromArgb(34, 197, 94),
            ToolState.Quiet => Color.FromArgb(245, 158, 11),
            _ => _secondaryText,
        };

        string metricText = tool.State == ToolState.Idle
            ? "--"
            : $"{tool.CpuPercent:0}% · {tool.RamMb:0} MB";

        var nameLabel = new Label
        {
            Text = tool.DisplayName,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = _primaryText,
            AutoSize = false,
            Size = new Size(120, 22),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
        };

        var stateLabel = new Label
        {
            Text = stateText,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = stateColor,
            AutoSize = false,
            Size = new Size(44, 22),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
        };

        var metricLabel = new Label
        {
            Text = metricText,
            Font = new Font("Segoe UI", 8f),
            ForeColor = _secondaryText,
            AutoSize = false,
            Size = new Size(cardWidth - 180, 22),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
        };

        card.Controls.Add(nameLabel);
        card.Controls.Add(stateLabel);
        card.Controls.Add(metricLabel);

        // Quota section
        if (supportsQuota && tool.Quota is { } quota)
        {
            bool displaysUsage = quota.DisplayKind == QuotaDisplayKind.Usage;
            QuotaFreshness freshness = quota.Freshness;

            string primaryText = quota.PrimaryPercent.HasValue
                ? $"{quota.PrimaryPercent.Value:0}%"
                : "--";

            string freshnessStr = freshness switch
            {
                QuotaFreshness.Live => "Live",
                QuotaFreshness.Stale => "Stale",
                _ => "N/A"
            };

            Color freshnessColor = freshness switch
            {
                QuotaFreshness.Live => Color.FromArgb(40, 180, 100),
                QuotaFreshness.Stale => Color.FromArgb(220, 140, 30),
                _ => Color.FromArgb(140, 140, 150),
            };

            string quotaLabel = displaysUsage && quota.InputTokens.HasValue
                ? $"In: {FormatTokenCount(quota.InputTokens)} Out: {FormatTokenCount(quota.OutputTokens)}"
                : $"{WindowLabel(quota.PrimaryWindowMinutes)}: {primaryText}";

            var quotaText = new Label
            {
                Text = quotaLabel,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = _primaryText,
                AutoSize = false,
                Size = new Size(cardWidth - 80, 18),
                Location = new Point(10, 30),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
            };

            var freshBadge = new Label
            {
                Text = $"[{freshnessStr}]",
                Font = new Font("Segoe UI", 7f, FontStyle.Bold),
                ForeColor = freshnessColor,
                AutoSize = false,
                Size = new Size(50, 18),
                Location = new Point(cardWidth - 70, 30),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent,
            };

            card.Controls.Add(quotaText);
            card.Controls.Add(freshBadge);

            // Progress bar
            if (!displaysUsage)
            {
                var progress = new MiniProgressBar
                {
                    Location = new Point(10, 52),
                    Size = new Size(cardWidth - 24, 6),
                    ValuePercent = quota.PrimaryPercent ?? 0,
                    BarColor = _pinkAccent,
                    Freshness = freshness,
                };
                card.Controls.Add(progress);
            }
        }

        return card;
    }

    // ──────────────────────── Tab painting ────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var clientRect = new Rectangle(0, 0, Width, Height);

        if (_state is SidebarState.Collapsed or SidebarState.Hovered)
        {
            DrawTab(g, clientRect);
        }
        else if (_animProgress > 0.01 && _animProgress < 0.99)
        {
            // During animation: draw tab portion + emerging panel
            DrawTab(g, new Rectangle(0, 0, Math.Min(TabHoverWidth + 4, Width), Height));
        }

        // When expanded, the panel controls handle their own painting
    }

    private void DrawTab(Graphics g, Rectangle bounds)
    {
        int tabW = _isHovered ? TabHoverWidth : TabWidth;
        var tabRect = new Rectangle(
            bounds.Right - tabW,
            bounds.Top + (bounds.Height - TabHeight) / 2,
            tabW,
            TabHeight);

        // Tab background with rounded left corners
        using var tabPath = CreateRoundedRectPath(tabRect, 6, 0, 0, 6);
        using (var bgBrush = new SolidBrush(_tabBackground))
        {
            g.FillPath(bgBrush, tabPath);
        }

        // Tab border (left and top/bottom edges only)
        using (var borderPen = new Pen(_tabBorder, 1f))
        {
            g.DrawPath(borderPen, tabPath);
        }

        // Left edge highlight line
        using (var hlPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f))
        {
            g.DrawLine(hlPen, tabRect.Left + 1, tabRect.Top + 2, tabRect.Left + 1, tabRect.Bottom - 2);
        }

        // Draw chevron (◀)
        int cx = tabRect.Left + tabW / 2;
        int cy = tabRect.Top + tabRect.Height / 2;
        int cs = ChevronSize;

        Color chevronColor = _isHovered ? _pinkAccent : _secondaryText;
        using (var chevPen = new Pen(chevronColor, 2f))
        {
            g.DrawLine(chevPen,
                cx + cs / 3, cy - cs / 2,
                cx - cs / 3, cy);
            g.DrawLine(chevPen,
                cx - cs / 3, cy,
                cx + cs / 3, cy + cs / 2);
        }

        // Active tools count dot (when tools are running)
        if (_snapshot is { RunningCount: > 0 } snap)
        {
            int dotSize = 6;
            int dotX = cx - dotSize / 2;
            int dotY = tabRect.Top + 14;

            using var glowBrush = new SolidBrush(Color.FromArgb(60, 34, 197, 94));
            g.FillEllipse(glowBrush, dotX - 1, dotY - 1, dotSize + 2, dotSize + 2);
            using var dotBrush = new SolidBrush(Color.FromArgb(34, 197, 94));
            g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
        }
    }

    // ──────────────────────── Theme ────────────────────────

    private void ApplyThemeColors()
    {
        if (_theme.HighContrast)
        {
            _surfaceColor = SystemColors.Window;
            _primaryText = SystemColors.WindowText;
            _secondaryText = SystemColors.GrayText;
            _pinkAccent = SystemColors.Highlight;
            _tabBackground = SystemColors.Control;
            _tabBorder = SystemColors.WindowFrame;
        }
        else if (_theme.IsDark)
        {
            _surfaceColor = Color.FromArgb(0x1C, 0x14, 0x1A);
            _primaryText = Color.FromArgb(0xFA, 0xEB, 0xF2);
            _secondaryText = Color.FromArgb(0xBE, 0xA0, 0xAF);
            _pinkAccent = Color.FromArgb(0xF5, 0x6E, 0xA0);
            _tabBackground = Color.FromArgb(0x28, 0x1E, 0x26);
            _tabBorder = Color.FromArgb(0x60, 0x40, 0x50);
        }
        else
        {
            _surfaceColor = Color.FromArgb(0xFF, 0xF5, 0xF8);
            _primaryText = Color.FromArgb(0x2D, 0x14, 0x23);
            _secondaryText = Color.FromArgb(0x6E, 0x46, 0x5A);
            _pinkAccent = Color.FromArgb(0xEB, 0x4B, 0x82);
            _tabBackground = Color.FromArgb(0xFF, 0xF0, 0xF4);
            _tabBorder = Color.FromArgb(0xF0, 0xC0, 0xD0);
        }

        BackColor = _theme.IsDark ? Color.FromArgb(0x14, 0x0E, 0x12) : Color.Black;
        ForeColor = _primaryText;
    }

    private static ThemeSettings ReadThemeSettings()
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);

        bool isLight = key?.GetValue("AppsUseLightTheme") is int light ? light != 0 : true;
        bool transparencyEnabled = key?.GetValue("EnableTransparency") is not int transparency || transparency != 0;

        return new ThemeSettings(
            IsDark: !isLight,
            TransparencyEnabled: transparencyEnabled,
            HighContrast: SystemInformation.HighContrast);
    }

    private readonly record struct ThemeSettings(bool IsDark, bool TransparencyEnabled, bool HighContrast);

    // ──────────────────────── Helpers ────────────────────────

    private static string WindowLabel(int? minutes)
    {
        if (!minutes.HasValue) return "Limit";
        return minutes.Value switch
        {
            <= 360 => $"{minutes.Value / 60.0:0.#}h Limit",
            <= 1560 => $"{minutes.Value / 60}h Limit",
            <= 10500 => "Weekly Limit",
            <= 46000 => "Monthly Limit",
            _ => $"{minutes.Value / 60}h Limit"
        };
    }

    private static string FormatTokenCount(long? tokens)
    {
        if (!tokens.HasValue) return "--";
        return tokens.Value switch
        {
            >= 1_000_000 => $"{tokens.Value / 1_000_000d:0.#}M",
            >= 1_000 => $"{tokens.Value / 1_000d:0.#}K",
            _ => tokens.Value.ToString()
        };
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int topLeft, int topRight, int bottomRight, int bottomLeft)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0) { path.AddRectangle(rect); return path; }

        int dTL = Math.Min(topLeft * 2, Math.Min(rect.Width, rect.Height));
        int dTR = Math.Min(topRight * 2, Math.Min(rect.Width, rect.Height));
        int dBR = Math.Min(bottomRight * 2, Math.Min(rect.Width, rect.Height));
        int dBL = Math.Min(bottomLeft * 2, Math.Min(rect.Width, rect.Height));

        if (dTL > 0) path.AddArc(rect.X, rect.Y, dTL, dTL, 180, 90);
        else path.AddLine(rect.X, rect.Y, rect.X + 1, rect.Y);

        if (dTR > 0) path.AddArc(rect.Right - dTR, rect.Y, dTR, dTR, 270, 90);
        else path.AddLine(rect.Right - 1, rect.Y, rect.Right, rect.Y);

        if (dBR > 0) path.AddArc(rect.Right - dBR, rect.Bottom - dBR, dBR, dBR, 0, 90);
        else path.AddLine(rect.Right, rect.Bottom - 1, rect.Right, rect.Bottom);

        if (dBL > 0) path.AddArc(rect.X, rect.Bottom - dBL, dBL, dBL, 90, 90);
        else path.AddLine(rect.X + 1, rect.Bottom, rect.X, rect.Bottom);

        path.CloseFigure();
        return path;
    }

    public new void Dispose()
    {
        _animationTimer.Stop();
        _animationTimer.Dispose();
        _mouseLeaveTimer.Stop();
        _mouseLeaveTimer.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ──────────────────────── Internal controls ────────────────────────

/// <summary>
/// A simple glass-morphism card panel for tool status rows.
/// </summary>
internal sealed class GlassSidePanel : Panel
{
    private readonly Color _bgColor;
    private readonly Color _borderColor;
    private readonly bool _isDark;

    public GlassSidePanel(int width, int height, Color bgColor, Color accentColor, bool isDark)
    {
        _bgColor = bgColor;
        _isDark = isDark;
        _borderColor = isDark
            ? Color.FromArgb(70, 180, 100, 130)
            : Color.FromArgb(90, 230, 170, 195);
        Width = width;
        Height = height;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = 8;
        using var path = CreateRoundedRectPath(rect, radius);

        using (var bgBrush = new SolidBrush(_bgColor))
            e.Graphics.FillPath(bgBrush, path);

        using (var borderPen = new Pen(_borderColor, 1f))
            e.Graphics.DrawPath(borderPen, path);

        using (var hlPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            e.Graphics.DrawLine(hlPen, radius, 1, rect.Right - radius, 1);
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0) { path.AddRectangle(rect); return path; }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Compact progress bar for quota display in the sidebar.
/// </summary>
internal sealed class MiniProgressBar : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double ValuePercent { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BarColor { get; set; } = Color.FromArgb(235, 75, 130);

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public QuotaFreshness Freshness { get; set; }

    public MiniProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Math.Max(2, Height / 2);

        // Track
        using var trackPath = CreateRoundedRectPath(rect, radius);
        using var trackBrush = new SolidBrush(Color.FromArgb(30, 0, 0, 0));
        e.Graphics.FillPath(trackBrush, trackPath);

        if (Freshness != QuotaFreshness.Unavailable && ValuePercent > 0)
        {
            double clamped = Math.Clamp(ValuePercent, 0, 100);
            int fillW = Math.Max(radius * 2, (int)(rect.Width * (clamped / 100.0)));
            fillW = Math.Min(fillW, rect.Width);

            var fillRect = new Rectangle(rect.X, rect.Y, fillW, rect.Height);
            using var fillPath = CreateRoundedRectPath(fillRect, radius);

            Color color = Freshness switch
            {
                QuotaFreshness.Stale => Color.FromArgb(220, 140, 30),
                _ => clamped switch
                {
                    > 92 => Color.FromArgb(225, 45, 70),
                    > 80 => Color.FromArgb(240, 120, 40),
                    _ => BarColor
                }
            };

            using var fillBrush = new SolidBrush(color);
            e.Graphics.FillPath(fillBrush, fillPath);
        }
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0) { path.AddRectangle(rect); return path; }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
