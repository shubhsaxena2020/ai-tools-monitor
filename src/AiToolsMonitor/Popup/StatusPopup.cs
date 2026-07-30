using AiToolsMonitor.Monitoring;
using Microsoft.Win32;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AiToolsMonitor.Popup;

/// <summary>
/// Right-docked glassmorphism sidebar panel displaying AI tool telemetry and usage limits.
/// </summary>
public sealed class StatusPopup : Form
{
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int CsDropShadow = 0x00020000;

    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtBottomRight = 17;

    private readonly HeaderPanel _headerPanel;
    private readonly FlowLayoutPanel _cardContainer;
    private readonly Panel _footerContainer;
    private readonly Label _lastUpdatedLabel;
    private readonly Label _runningCountBadge;
    private readonly Label _todaySummaryLabel;

    private ThemeSettings _theme;
    private BackdropMode _backdropMode;
    private Color _primaryText;
    private Color _secondaryText;
    private Color _cardBackground;
    private Color _cardBorder;
    private Color _pinkAccent;
    private Color _fallbackSurface;

    public StatusPopup()
    {
        _theme = ReadThemeSettings();

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;

        Opacity = 1.0;
        BackColor = Color.Black;
        Width = 350;

        DoubleBuffered = false;

        Deactivate += (_, _) => Hide();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Hide();
        };

        // Header Panel
        _headerPanel = new HeaderPanel();

        // Footer Panel
        _footerContainer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                Padding = new Padding(16, 8, 16, 8),
                BackColor = Color.Transparent,
            };

        _lastUpdatedLabel = new Label
        {
            Text = "Last updated: --:--:--",
            Font = new Font("Segoe UI", 8f),
            Dock = DockStyle.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            UseMnemonic = false,
            UseCompatibleTextRendering = false,
        };

        _runningCountBadge = new Label
        {
            Text = "0 Active",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Dock = DockStyle.Right,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
            UseMnemonic = false,
            UseCompatibleTextRendering = false,
        };

        _footerContainer.Controls.Add(_lastUpdatedLabel);
            _footerContainer.Controls.Add(_runningCountBadge);

            _todaySummaryLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 24),
                BackColor = Color.Transparent,
                Visible = false,
            };
            _footerContainer.Controls.Add(_todaySummaryLabel);

            // Center scrollable card list container
        _cardContainer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 4, 12, 4),
            BackColor = Color.Transparent,
        };

        Controls.Add(_cardContainer);
        Controls.Add(_footerContainer);
        Controls.Add(_headerPanel);

        ApplyThemeColors();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.Style |= WsCaption | WsThickFrame;
            cp.ExStyle |= WsExToolWindow;
            cp.ExStyle &= ~WsExLayered;
            cp.ClassStyle |= CsDropShadow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _theme = ReadThemeSettings();
        ApplyThemeColors();
        ApplyBackdrop();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcCalcSize && m.WParam != nint.Zero)
        {
            m.Result = nint.Zero;
            return;
        }

        if (m.Msg == WmNcHitTest)
        {
            base.WndProc(ref m);
            long hit = m.Result.ToInt64();
            if (hit >= HtLeft && hit <= HtBottomRight)
                m.Result = (nint)HtClient;
            return;
        }

        base.WndProc(ref m);
    }

    private void ApplyBackdrop()
    {
        _backdropMode = BackdropMode.None;

        bool windows11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        bool supportsSystemBackdrop = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

        if (windows11)
        {
            int dark = _theme.IsDark ? 1 : 0;
            SetDwmAttribute(DwmWindowAttribute.UseImmersiveDarkMode, dark);

            int corner = (int)DwmWindowCornerPreference.Round;
            SetDwmAttribute(DwmWindowAttribute.WindowCornerPreference, corner);

            Color border = _theme.HighContrast
                ? SystemColors.WindowFrame
                : _theme.IsDark
                    ? Color.FromArgb(0x60, 0x40, 0x50)
                    : Color.FromArgb(0xF0, 0xC0, 0xD0);

            int borderColor = ToColorRef(border);
            SetDwmAttribute(DwmWindowAttribute.BorderColor, borderColor);
        }

        bool allowGlass = !_theme.HighContrast && _theme.TransparencyEnabled;

        if (allowGlass)
        {
            var margins = new Margins(-1);
            int extendResult = DwmExtendFrameIntoClientArea(Handle, ref margins);

            if (extendResult >= 0 && supportsSystemBackdrop)
            {
                int acrylic = (int)DwmSystemBackdropType.TransientWindow;
                if (SetDwmAttribute(DwmWindowAttribute.SystemBackdropType, acrylic))
                {
                    _backdropMode = BackdropMode.SystemAcrylic;
                }
            }

            if (_backdropMode == BackdropMode.None && OperatingSystem.IsWindowsVersionAtLeast(10))
            {
                if (TrySetLegacyAccent(AccentState.EnableAcrylicBlurBehind))
                {
                    _backdropMode = BackdropMode.LegacyAccent;
                }
                else if (TrySetLegacyAccent(AccentState.EnableBlurBehind))
                {
                    _backdropMode = BackdropMode.LegacyAccent;
                }
            }
        }

        BackColor = _backdropMode == BackdropMode.None
            ? _fallbackSurface
            : Color.Black;

        Invalidate(true);
    }

    private bool TrySetLegacyAccent(AccentState state)
    {
        Color tint = _theme.IsDark
            ? Color.FromArgb(0xD0, 0x1C, 0x14, 0x1A)
            : Color.FromArgb(0xD0, 0xFF, 0xF0, 0xF5);

        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == AccentState.EnableAcrylicBlurBehind ? 0u : 2u,
            GradientColor = ToAbgr(tint),
            AnimationId = 0,
        };

        int policySize = Marshal.SizeOf<AccentPolicy>();
        nint policyPointer = Marshal.AllocHGlobal(policySize);

        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);

            var data = new WindowCompositionAttribData
            {
                Attribute = WindowCompositionAttribute.AccentPolicy,
                Data = policyPointer,
                SizeOfData = (uint)policySize,
            };

            return SetWindowCompositionAttribute(Handle, ref data);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    private bool SetDwmAttribute(DwmWindowAttribute attribute, int value)
    {
        return DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int)) >= 0;
    }

    private void ApplyThemeColors()
    {
        if (_theme.HighContrast)
        {
            _fallbackSurface = SystemColors.Window;
            _primaryText = SystemColors.WindowText;
            _secondaryText = SystemColors.GrayText;
            _cardBackground = SystemColors.Control;
            _cardBorder = SystemColors.WindowFrame;
            _pinkAccent = SystemColors.Highlight;
        }
        else if (_theme.IsDark)
        {
            // Dark Plum & Pink Glass palette
            _fallbackSurface = Color.FromArgb(0x1C, 0x14, 0x1A);
            _primaryText = Color.FromArgb(0xFA, 0xEB, 0xF2);
            _secondaryText = Color.FromArgb(0xBE, 0xA0, 0xAF);
            _cardBackground = Color.FromArgb(140, 42, 30, 40);
            _cardBorder = Color.FromArgb(70, 180, 100, 130);
            _pinkAccent = Color.FromArgb(0xF5, 0x6E, 0xA0);
        }
        else
        {
            // Pink & White Glassmorphism palette
            _fallbackSurface = Color.FromArgb(0xFF, 0xF5, 0xF8);
            _primaryText = Color.FromArgb(0x2D, 0x14, 0x23);
            _secondaryText = Color.FromArgb(0x6E, 0x46, 0x5A);
            _cardBackground = Color.FromArgb(180, 255, 255, 255);
            _cardBorder = Color.FromArgb(90, 230, 170, 195);
            _pinkAccent = Color.FromArgb(0xEB, 0x4B, 0x82);
        }

        ForeColor = _primaryText;
        _headerPanel.TitleColor = _pinkAccent;
        _headerPanel.SubtitleColor = _secondaryText;
        _headerPanel.Invalidate();
        _lastUpdatedLabel.ForeColor = _secondaryText;
        _runningCountBadge.ForeColor = _pinkAccent;
        _todaySummaryLabel.ForeColor = _pinkAccent;
    }

    public void Render(StatusSnapshot snapshot)
    {
        _cardContainer.SuspendLayout();
        _cardContainer.Controls.Clear();

        foreach (var tool in snapshot.Tools)
        {
            var card = CreateToolCard(tool);
            _cardContainer.Controls.Add(card);
        }

        _lastUpdatedLabel.Text = $"Updated {snapshot.SampledAtUtc.ToLocalTime():HH:mm:ss}";
        _runningCountBadge.Text = $"{snapshot.RunningCount} Running";

        _cardContainer.ResumeLayout(true);
    }

    private Panel CreateToolCard(ToolStatus tool)
    {
        bool supportsQuota = tool.Quota is not null;
        int cardHeight = supportsQuota ? 120 : 54;
        int cardWidth = 302;

        var card = new GlassCardPanel
        {
            Width = cardWidth,
            Height = cardHeight,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = _cardBackground,
            BorderColor = _cardBorder,
        };

        // Header Row Container
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        // 1. Tool Icon with GDI+ glyph & status dot (20x20)
        var iconControl = new ToolIconControl
        {
            DisplayName = tool.DisplayName,
            State = tool.State,
            AccentColor = _pinkAccent,
            IsDark = _theme.IsDark,
            Location = new Point(0, 3),
            Size = new Size(20, 20),
        };

        // 2. Tool Name
        var nameLabel = new Label
        {
            Text = tool.DisplayName,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = _primaryText,
            Location = new Point(26, 1),
            Size = new Size(118, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
        };

        // 3. Status Badge Tag
        var stateBadge = new StatusBadgeTag
        {
            State = tool.State,
            IsDark = _theme.IsDark,
            Location = new Point(144, 4),
            Size = new Size(52, 18),
        };

        // 4. Metrics (CPU / RAM)
        string metricText = tool.State == ToolState.Idle
            ? "--"
            : $"{tool.CpuPercent:0}% · {tool.RamMb:0} MB";

        var metricsLabel = new Label
        {
            Text = metricText,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = _secondaryText,
            Location = new Point(196, 1),
            Size = new Size(82, 24),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
        };

        headerPanel.Controls.Add(iconControl);
        headerPanel.Controls.Add(nameLabel);
        headerPanel.Controls.Add(stateBadge);
        headerPanel.Controls.Add(metricsLabel);
        card.Controls.Add(headerPanel);

        // Quota Section
        if (supportsQuota)
        {
            var quotaPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
            };

            var quota = tool.Quota;
            QuotaFreshness freshness = quota?.Freshness ?? QuotaFreshness.Unavailable;
            bool displaysUsage = quota?.DisplayKind == QuotaDisplayKind.Usage;

            string primaryText = quota?.PrimaryPercent.HasValue == true
                ? $"{quota.PrimaryPercent.Value:0}%"
                : "--";

            string secondaryText = quota?.SecondaryPercent.HasValue == true
                ? $"{quota.SecondaryPercent.Value:0}%"
                : "--";

            string freshnessBadge = freshness switch
            {
                QuotaFreshness.Live => "Live",
                QuotaFreshness.Stale => "Stale",
                QuotaFreshness.Unavailable => "Unavailable",
                _ => "Unavailable"
            };

            Color freshnessColor = freshness switch
            {
                QuotaFreshness.Live => Color.FromArgb(40, 180, 100),
                QuotaFreshness.Stale => Color.FromArgb(220, 140, 30),
                QuotaFreshness.Unavailable => Color.FromArgb(140, 140, 150),
                _ => Color.Gray
            };

            bool hasSecondaryWindow = freshness != QuotaFreshness.Live || quota?.SecondaryPercent.HasValue == true;

            // Row 1: Primary Window / Token Usage (Left) + Freshness Badge (Right)
            var primaryLabel = new Label
            {
                Text = displaysUsage
                    ? $"Input: {FormatTokenCount(quota?.InputTokens)}  Output: {FormatTokenCount(quota?.OutputTokens)}"
                    : $"{WindowLabel(quota?.PrimaryWindowMinutes)}:  {primaryText}",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = _primaryText,
                Location = new Point(0, 28),
                Size = new Size(198, 18),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
            };

            var badgeLabel = new Label
            {
                Text = $"[{freshnessBadge}]",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = freshnessColor,
                Location = new Point(200, 28),
                Size = new Size(78, 18),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent,
            };

            // Row 2: Secondary Window / Usage Details (Left) + Resets In (Right)
            var secondaryLabel = new Label
            {
                Text = displaysUsage
                    ? FormatUsageDetails(quota)
                    : hasSecondaryWindow
                        ? $"{WindowLabel(quota?.SecondaryWindowMinutes)}:  {secondaryText}"
                        : "No secondary limit",
                Font = new Font("Segoe UI", 8.25f, hasSecondaryWindow || displaysUsage ? FontStyle.Bold : FontStyle.Italic),
                ForeColor = hasSecondaryWindow || displaysUsage ? _primaryText : _secondaryText,
                Location = new Point(0, 48),
                Size = new Size(155, 18),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
            };

            quotaPanel.Controls.Add(primaryLabel);
            quotaPanel.Controls.Add(badgeLabel);
            quotaPanel.Controls.Add(secondaryLabel);

            if (!displaysUsage &&
                quota?.ResetsAt.HasValue == true &&
                freshness != QuotaFreshness.Unavailable)
            {
                var remaining = quota.ResetsAt.Value - DateTimeOffset.UtcNow;
                string resetStr = remaining.TotalMinutes > 0
                    ? $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m"
                    : "Reset imminent";

                var resetLabel = new Label
                {
                    Text = resetStr,
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Italic),
                    ForeColor = _secondaryText,
                    Location = new Point(156, 48),
                    Size = new Size(122, 18),
                    TextAlign = ContentAlignment.MiddleRight,
                    BackColor = Color.Transparent,
                };
                quotaPanel.Controls.Add(resetLabel);
            }

            if (!displaysUsage)
            {
                var progressBar = new QuotaProgressBar
                {
                    Location = new Point(0, 68),
                    Size = new Size(278, 7),
                    ValuePercent = quota?.PrimaryPercent ?? 0,
                    BarColor = _pinkAccent,
                    Freshness = freshness,
                    IsDark = _theme.IsDark,
                };
                quotaPanel.Controls.Add(progressBar);
            }

            card.Controls.Add(quotaPanel);
            quotaPanel.BringToFront();
        }

        return card;
    }

    private static string WindowLabel(int? minutes)
    {
        if (!minutes.HasValue)
            return "Limit";

        double hours = minutes.Value / 60.0;
        return minutes.Value switch
        {
            <= 360 => $"{hours:0.#}h Limit",
            <= 1560 => $"{hours:0}h Limit",
            <= 10500 => "Weekly Limit",
            <= 46000 => "Monthly Limit",
            _ => $"{hours:0}h Limit"
        };
    }

    private static string FormatUsageDetails(ToolQuota? quota)
    {
        string total = FormatTokenCount(quota?.TotalTokens);
        return quota?.CostUsd.HasValue == true
            ? $"Total: {total}  Cost: ${quota.CostUsd.Value:0.0000}"
            : $"Total: {total}";
    }

    private static string FormatTokenCount(long? tokens)
    {
        if (!tokens.HasValue)
            return "--";

        return tokens.Value switch
        {
            >= 1_000_000 => $"{tokens.Value / 1_000_000d:0.#}M",
            >= 1_000 => $"{tokens.Value / 1_000d:0.#}K",
            _ => tokens.Value.ToString()
        };
    }

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

    public void ShowNearTray()
    {
        ThemeSettings currentTheme = ReadThemeSettings();

        if (currentTheme != _theme)
        {
            _theme = currentTheme;
            ApplyThemeColors();
            if (IsHandleCreated)
                ApplyBackdrop();
        }

        var workingArea = Screen.PrimaryScreen!.WorkingArea;
        Width = 350;
        Height = workingArea.Height;
        Location = new Point(workingArea.Right - Width, workingArea.Top);

        Show();
        Activate();
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

    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }

    private static uint ToAbgr(Color color)
    {
        return ((uint)color.A << 24) |
               ((uint)color.B << 16) |
               ((uint)color.G << 8) |
               color.R;
    }

    private enum BackdropMode
    {
        None,
        SystemAcrylic,
        LegacyAccent,
    }

    private readonly record struct ThemeSettings(
        bool IsDark,
        bool TransparencyEnabled,
        bool HighContrast);

    private enum DwmWindowAttribute
    {
        UseImmersiveDarkMode = 20,
        WindowCornerPreference = 33,
        BorderColor = 34,
        SystemBackdropType = 38,
    }

    private enum DwmSystemBackdropType
    {
        Auto = 0,
        None = 1,
        MainWindow = 2,
        TransientWindow = 3,
        TabbedWindow = 4,
    }

    private enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }

    private enum WindowCompositionAttribute
    {
        AccentPolicy = 0x13,
    }

    private enum AccentState
    {
        Disabled = 0,
        EnableGradient = 1,
        EnableTransparentGradient = 2,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
        EnableHostBackdrop = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;

        public Margins(int all)
        {
            Left = Right = Top = Bottom = all;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData
    {
        public WindowCompositionAttribute Attribute;
        public nint Data;
        public uint SizeOfData;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        DwmWindowAttribute attribute,
        ref int value,
        uint valueSize);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmExtendFrameIntoClientArea(
        nint hwnd,
        ref Margins margins);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(
        nint hwnd,
        ref WindowCompositionAttribData data);
}

/// <summary>
/// Custom glass card panel control with subtle rounded border drawing and glass highlight.
/// </summary>
internal sealed class GlassCardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(90, 230, 170, 195);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 8;

    public GlassCardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (Width <= 0 || Height <= 0) return;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectanglePath(rect, CornerRadius);

        using (var bgBrush = new SolidBrush(BackColor))
        {
            e.Graphics.FillPath(bgBrush, path);
        }

        using (var borderPen = new Pen(BorderColor, 1f))
        {
            e.Graphics.DrawPath(borderPen, path);
        }

        using (var highlightPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
        {
            e.Graphics.DrawLine(highlightPen, rect.X + CornerRadius, rect.Y + 1, rect.Right - CornerRadius, rect.Y + 1);
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}

/// <summary>
/// Lightweight custom progress bar with rounded pill caps and gradient fill for quota percentage.
/// </summary>
internal sealed class QuotaProgressBar : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ValuePercent { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BarColor { get; set; } = Color.FromArgb(235, 75, 130);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public QuotaFreshness Freshness { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsDark { get; set; }

    public QuotaProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (Width <= 0 || Height <= 0) return;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Math.Max(2, Height / 2);

        // Track background
        using var trackPath = CreateRoundedRectanglePath(rect, radius);
        Color trackColor = IsDark
            ? Color.FromArgb(40, 200, 200, 220)
            : Color.FromArgb(30, 0, 0, 0);
        using var trackBrush = new SolidBrush(trackColor);
        e.Graphics.FillPath(trackBrush, trackPath);

        if (Freshness != QuotaFreshness.Unavailable && ValuePercent > 0)
        {
            double clampedPercent = Math.Clamp(ValuePercent, 0, 100);
            int fillWidth = Math.Max(radius * 2, (int)(rect.Width * (clampedPercent / 100.0)));
            fillWidth = Math.Min(fillWidth, rect.Width);

            var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);
            using var fillPath = CreateRoundedRectanglePath(fillRect, radius);

            Color colorToUse = Freshness switch
            {
                QuotaFreshness.Stale => Color.FromArgb(220, 140, 30),
                _ => clampedPercent switch
                {
                    > 92 => Color.FromArgb(225, 45, 70),   // High usage red/rose
                    > 80 => Color.FromArgb(240, 120, 40),  // Warning amber/coral
                    _ => BarColor                          // Pink accent
                }
            };

            Color colorBrighter = Color.FromArgb(
                colorToUse.A,
                Math.Min(255, colorToUse.R + 35),
                Math.Min(255, colorToUse.G + 35),
                Math.Min(255, colorToUse.B + 35));

            using var fillBrush = new LinearGradientBrush(fillRect, colorToUse, colorBrighter, LinearGradientMode.Horizontal);
            e.Graphics.FillPath(fillBrush, fillPath);

            // Subtle 1px top highlight line inside fill path for glass polish
            using var highlightPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
            e.Graphics.DrawLine(highlightPen, fillRect.X + radius, fillRect.Y + 1, fillRect.Right - radius, fillRect.Y + 1);
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}

/// <summary>
/// Renders tool-specific GDI+ vector glyphs alongside a status dot badge.
/// </summary>
internal sealed class ToolIconControl : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string DisplayName { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ToolState State { get; set; } = ToolState.Idle;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentColor { get; set; } = Color.FromArgb(235, 75, 130);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsDark { get; set; }

    public ToolIconControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Color.Transparent;
        Size = new Size(20, 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var iconRect = new Rectangle(0, 2, 14, 14);
        DrawToolGlyph(e.Graphics, DisplayName, iconRect, AccentColor);

        Color dotColor = State switch
        {
            ToolState.Active => Color.FromArgb(34, 197, 94),
            ToolState.Quiet => Color.FromArgb(245, 158, 11),
            ToolState.Idle => IsDark ? Color.FromArgb(160, 160, 175) : Color.FromArgb(130, 130, 140),
            _ => Color.Gray
        };

        int dotSize = 5;
        int dotX = 13;
        int dotY = 13;

        if (State == ToolState.Active || State == ToolState.Quiet)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(60, dotColor));
            e.Graphics.FillEllipse(glowBrush, dotX - 1, dotY - 1, dotSize + 2, dotSize + 2);
            using var dotBrush = new SolidBrush(dotColor);
            e.Graphics.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
        }
        else
        {
            using var pen = new Pen(dotColor, 1.2f);
            e.Graphics.DrawEllipse(pen, dotX, dotY, dotSize, dotSize);
        }
    }

    private static void DrawToolGlyph(Graphics g, string displayName, Rectangle rect, Color accent)
    {
        string name = displayName.ToLowerInvariant();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (name.Contains("claude"))
        {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float outerR = rect.Width / 2f - 0.5f;
            float innerR = outerR * 0.35f;

            PointF[] points = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4.0 - Math.PI / 2.0;
                float r = (i % 2 == 0) ? outerR : innerR;
                points[i] = new PointF(
                    cx + (float)(r * Math.Cos(angle)),
                    cy + (float)(r * Math.Sin(angle)));
            }
            using var path = new GraphicsPath();
            path.AddPolygon(points);
            using var brush = new SolidBrush(accent);
            g.FillPath(brush, path);
        }
        else if (name.Contains("hermes"))
        {
            using var pen = new Pen(accent, 1.4f);
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;

            g.DrawArc(pen, rect.X + 0.5f, rect.Y + 2f, 6.5f, 9.5f, 130, 190);
            g.DrawArc(pen, rect.X + 7f, rect.Y + 2f, 6.5f, 9.5f, 220, 190);

            using var brush = new SolidBrush(accent);
            PointF[] diamond = [
                new PointF(cx, cy - 3.5f),
                new PointF(cx + 2.5f, cy),
                new PointF(cx, cy + 3.5f),
                new PointF(cx - 2.5f, cy)
            ];
            g.FillPolygon(brush, diamond);
        }
        else if (name.Contains("codex"))
        {
            using var pen = new Pen(accent, 1.25f);
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float r = 4.2f;

            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            g.DrawEllipse(pen, cx - r + 1.8f, cy - r - 1.2f, r * 2, r * 2);
            g.DrawEllipse(pen, cx - r - 1.8f, cy - r - 1.2f, r * 2, r * 2);
        }
        else if (name.Contains("opencode"))
        {
            using var pen = new Pen(accent, 1.3f);
            var frame = new RectangleF(rect.X + 0.5f, rect.Y + 1f, 13f, 12f);
            using var path = CreateRoundedPathF(frame, 2.5f);
            g.DrawPath(pen, path);

            g.DrawLines(pen, [
                new PointF(rect.X + 3.5f, rect.Y + 4.5f),
                new PointF(rect.X + 6.2f, rect.Y + 7.0f),
                new PointF(rect.X + 3.5f, rect.Y + 9.5f)
            ]);
            g.DrawLine(pen, rect.X + 7.8f, rect.Y + 9.5f, rect.X + 11.0f, rect.Y + 9.5f);
        }
        else if (name.Contains("antigravity"))
        {
            using var pen = new Pen(accent, 1.3f);
            float cx = rect.X + rect.Width / 2f;

            PointF[] tri = [
                new PointF(rect.X + 2.5f, rect.Y + 3f),
                new PointF(rect.X + 11.5f, rect.Y + 3f),
                new PointF(cx, rect.Y + 11f)
            ];
            g.DrawPolygon(pen, tri);
            g.DrawEllipse(pen, rect.X, rect.Y + 5.5f, 14f, 4.5f);
        }
        else
        {
            using var pen = new Pen(accent, 1.4f);
            g.DrawEllipse(pen, rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
        }
    }

    private static GraphicsPath CreateRoundedPathF(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Renders a styled status pill badge with soft background fill and theme-aware contrast text.
/// </summary>
internal sealed class StatusBadgeTag : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ToolState State { get; set; } = ToolState.Idle;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsDark { get; set; }

    public StatusBadgeTag()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Color.Transparent;
        Size = new Size(52, 18);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (Width <= 0 || Height <= 0) return;

        (Color textColor, Color bgFill) = (State, IsDark) switch
        {
            (ToolState.Active, true) => (Color.FromArgb(74, 222, 128), Color.FromArgb(45, 20, 80, 40)),
            (ToolState.Active, false) => (Color.FromArgb(22, 128, 61), Color.FromArgb(30, 34, 197, 94)),
            (ToolState.Quiet, true) => (Color.FromArgb(251, 191, 36), Color.FromArgb(45, 80, 55, 10)),
            (ToolState.Quiet, false) => (Color.FromArgb(180, 105, 10), Color.FromArgb(30, 245, 158, 11)),
            (ToolState.Idle, true) => (Color.FromArgb(160, 160, 175), Color.FromArgb(35, 70, 70, 80)),
            (ToolState.Idle, false) => (Color.FromArgb(110, 110, 125), Color.FromArgb(25, 140, 140, 150)),
            _ => (Color.Gray, Color.FromArgb(20, 128, 128, 128))
        };

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedPath(rect, 4);

        using (var bgBrush = new SolidBrush(bgFill))
        {
            e.Graphics.FillPath(bgBrush, path);
        }

        using (var borderPen = new Pen(Color.FromArgb(70, textColor), 1f))
        {
            e.Graphics.DrawPath(borderPen, path);
        }

        string text = State.ToString().ToUpperInvariant();
        using var font = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var textBrush = new SolidBrush(textColor);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        e.Graphics.DrawString(text, font, textBrush, rect, sf);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Custom header panel control rendering anti-aliased title and subtitle text on glass background.
/// </summary>
internal sealed class HeaderPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TitleColor { get; set; } = Color.FromArgb(245, 110, 160);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SubtitleColor { get; set; } = Color.FromArgb(190, 160, 175);

    public HeaderPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        Dock = DockStyle.Top;
        Height = 82;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var titleFont = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(TitleColor);
        e.Graphics.DrawString("AI Tools Monitor", titleFont, titleBrush, new PointF(16, 28));

        using var subFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var subBrush = new SolidBrush(SubtitleColor);
        e.Graphics.DrawString("Live Telemetry & Quota Limits", subFont, subBrush, new PointF(16, 52));
    }
}


