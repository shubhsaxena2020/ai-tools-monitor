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

    private readonly Panel _headerContainer;
    private readonly Label _headerTitle;
    private readonly Label _headerSubtitle;
    private readonly FlowLayoutPanel _cardContainer;
    private readonly Panel _footerContainer;
    private readonly Label _lastUpdatedLabel;
    private readonly Label _runningCountBadge;

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
        _headerContainer = new Panel
        {
            Dock = DockStyle.Top,
            Height = 65,
            Padding = new Padding(16, 12, 16, 8),
            BackColor = Color.Transparent,
        };

        _headerTitle = new Label
        {
            Text = "AI Tools Monitor",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 10),
            BackColor = Color.Transparent,
        };

        _headerSubtitle = new Label
        {
            Text = "Live Telemetry & Quota Limits",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(16, 34),
            BackColor = Color.Transparent,
        };

        _headerContainer.Controls.Add(_headerTitle);
        _headerContainer.Controls.Add(_headerSubtitle);

        // Footer Panel
        _footerContainer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
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
        };

        _runningCountBadge = new Label
        {
            Text = "0 Active",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Dock = DockStyle.Right,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
        };

        _footerContainer.Controls.Add(_lastUpdatedLabel);
        _footerContainer.Controls.Add(_runningCountBadge);

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
        Controls.Add(_headerContainer);
        Controls.Add(_footerContainer);

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
        _headerTitle.ForeColor = _pinkAccent;
        _headerSubtitle.ForeColor = _secondaryText;
        _lastUpdatedLabel.ForeColor = _secondaryText;
        _runningCountBadge.ForeColor = _pinkAccent;
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
        int cardHeight = supportsQuota ? 122 : 62;

        var card = new GlassCardPanel
        {
            Width = 308,
            Height = cardHeight,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12, 10, 12, 10),
            BackColor = _cardBackground,
            BorderColor = _cardBorder,
        };

        // Header Row: Dot + DisplayName + State + CPU/RAM
        Color dotColor = tool.State switch
        {
            ToolState.Idle => Color.FromArgb(150, 150, 160),
            ToolState.Quiet => Color.FromArgb(70, 160, 220),
            ToolState.Active => Color.FromArgb(40, 190, 110),
            _ => Color.Gray,
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 26,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var nameLabel = new Label
        {
            Text = "●  " + tool.DisplayName,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = dotColor,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
        };

        var stateLabel = new Label
        {
            Text = tool.State.ToString(),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = _primaryText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
        };

        string metricText = tool.State == ToolState.Idle
            ? "--"
            : $"{tool.CpuPercent:0}% · {tool.RamMb:0} MB";

        var metricsLabel = new Label
        {
            Text = metricText,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = _secondaryText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent,
        };

        headerLayout.Controls.Add(nameLabel, 0, 0);
        headerLayout.Controls.Add(stateLabel, 1, 0);
        headerLayout.Controls.Add(metricsLabel, 2, 0);
        card.Controls.Add(headerLayout);

        // Quota Section for Codex and Claude Code
        if (supportsQuota)
        {
            var quotaPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 0),
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

            // Quota Row 1: 5-Hr Limit
            var primaryLabel = new Label
            {
                Text = displaysUsage
                    ? $"Input: {FormatTokenCount(quota?.InputTokens)}  Output: {FormatTokenCount(quota?.OutputTokens)}"
                    : $"5-Hr Limit:  {primaryText}",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = _primaryText,
                Location = new Point(0, 28),
                AutoSize = true,
                BackColor = Color.Transparent,
            };

            var badgeLabel = new Label
            {
                Text = $"[{freshnessBadge}]",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = freshnessColor,
                Location = new Point(220, 28),
                AutoSize = true,
                BackColor = Color.Transparent,
            };

            // Quota Row 2: Weekly Limit
            var secondaryLabel = new Label
            {
                Text = displaysUsage
                    ? FormatUsageDetails(quota)
                    : $"Weekly:       {secondaryText}",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = _primaryText,
                Location = new Point(0, 48),
                AutoSize = true,
                BackColor = Color.Transparent,
            };

            // Reset time indicator if resetsAt is present
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
                    Location = new Point(180, 48),
                    AutoSize = true,
                    BackColor = Color.Transparent,
                };
                quotaPanel.Controls.Add(resetLabel);
            }

            quotaPanel.Controls.Add(primaryLabel);
            quotaPanel.Controls.Add(badgeLabel);
            quotaPanel.Controls.Add(secondaryLabel);

            if (!displaysUsage)
            {
                // Quota Usage Bar (drawn based on primary limit)
                var progressBar = new QuotaProgressBar
                {
                    Location = new Point(0, 68),
                    Size = new Size(284, 6),
                    ValuePercent = quota?.PrimaryPercent ?? 0,
                    BarColor = _pinkAccent,
                    Freshness = freshness,
                };
                quotaPanel.Controls.Add(progressBar);
            }

            card.Controls.Add(quotaPanel);
            quotaPanel.BringToFront();
        }

        return card;
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
/// Custom glass card panel control with subtle border drawing.
/// </summary>
internal sealed class GlassCardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(90, 230, 170, 195);

    public GlassCardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor, 1f);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawRectangle(pen, rect);
    }
}

/// <summary>
/// Lightweight custom progress bar for quota percentage.
/// </summary>
internal sealed class QuotaProgressBar : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ValuePercent { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BarColor { get; set; } = Color.FromArgb(235, 75, 130);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public QuotaFreshness Freshness { get; set; }

    public QuotaProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var trackRect = new Rectangle(0, 0, Width, Height);
        using var trackBrush = new SolidBrush(Color.FromArgb(50, 180, 180, 190));
        e.Graphics.FillRectangle(trackBrush, trackRect);

        if (Freshness != QuotaFreshness.Unavailable && ValuePercent > 0)
        {
            int fillWidth = (int)Math.Clamp(Width * (ValuePercent / 100.0), 4, Width);
            var fillRect = new Rectangle(0, 0, fillWidth, Height);

            Color colorToUse = Freshness switch
            {
                QuotaFreshness.Stale => Color.FromArgb(220, 140, 30),
                _ => BarColor
            };

            using var fillBrush = new SolidBrush(colorToUse);
            e.Graphics.FillRectangle(fillBrush, fillRect);
        }
    }
}
