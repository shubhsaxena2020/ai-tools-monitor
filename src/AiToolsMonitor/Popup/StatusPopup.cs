using AiToolsMonitor.Monitoring;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace AiToolsMonitor.Popup;

/// <summary>Borderless popup shown near the tray icon on left-click.</summary>
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

    private readonly Label _header;
    private readonly TableLayoutPanel _rows;
    private readonly Label _lastUpdated;

    private ThemeSettings _theme;
    private BackdropMode _backdropMode;
    private Color _primaryText;
    private Color _secondaryText;
    private Color _fallbackSurface;

    public StatusPopup()
    {
        _theme = ReadThemeSettings();

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;

        // Keep the HWND fully opaque. DWM supplies translucency.
        Opacity = 1.0;
        BackColor = Color.Black;
        Padding = new Padding(1);
        Width = 340;
        Height = 210;

        // Do not enable form-level OptimizedDoubleBuffer/WS_EX_COMPOSITED --
        // an opaque back buffer can cover the DWM backdrop surface.
        DoubleBuffered = false;

        Deactivate += (_, _) => Hide();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Hide();
        };

        _header = new Label
        {
            Text = "AI Tools Monitor",
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Color.Transparent,
        };

        _rows = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = ToolProfile.Defaults.Length,
            AutoSize = false,
            Height = 26 * ToolProfile.Defaults.Length,

            // The old opaque RGB(32,32,32) would cover the backdrop.
            BackColor = Color.Transparent,
        };
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        _lastUpdated = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            Font = new Font("Segoe UI", 8),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Color.Transparent,
        };

        Controls.Add(_rows);
        Controls.Add(_header);
        Controls.Add(_lastUpdated);

        ApplyThemeColors();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;

            // These frame hints let DWM retain its native rounded clipping and
            // shadow, while WM_NCCALCSIZE below makes the whole area client-area.
            cp.Style |= WsCaption | WsThickFrame;

            // Keep the popup out of Alt+Tab/taskbar switching.
            cp.ExStyle |= WsExToolWindow;

            // Layered HWNDs interfere with native DWM rounding/backdrops.
            cp.ExStyle &= ~WsExLayered;

            // Classic fallback; DWM owns the shadow on composited Windows 11.
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
        // Remove the visible caption/sizing frame while retaining its DWM hints.
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
        bool supportsSystemBackdrop =
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

        if (windows11)
        {
            int dark = _theme.IsDark ? 1 : 0;
            SetDwmAttribute(DwmWindowAttribute.UseImmersiveDarkMode, dark);

            int corner = (int)DwmWindowCornerPreference.Round;
            SetDwmAttribute(DwmWindowAttribute.WindowCornerPreference, corner);

            // COLORREF has no alpha: these are the precomposited equivalents
            // of white/20% over #181818 and black/14% over #F5F5F5.
            Color border = _theme.HighContrast
                ? SystemColors.WindowFrame
                : _theme.IsDark
                    ? Color.FromArgb(0x46, 0x46, 0x46)
                    : Color.FromArgb(0xD3, 0xD3, 0xD3);

            int borderColor = ToColorRef(border);
            SetDwmAttribute(DwmWindowAttribute.BorderColor, borderColor);
        }

        bool allowGlass =
            !_theme.HighContrast &&
            _theme.TransparencyEnabled;

        if (allowGlass)
        {
            var margins = new Margins(-1);
            int extendResult =
                DwmExtendFrameIntoClientArea(Handle, ref margins);

            if (extendResult >= 0 && supportsSystemBackdrop)
            {
                int acrylic = (int)DwmSystemBackdropType.TransientWindow;

                if (SetDwmAttribute(
                    DwmWindowAttribute.SystemBackdropType,
                    acrylic))
                {
                    _backdropMode = BackdropMode.SystemAcrylic;
                }
            }

            if (_backdropMode == BackdropMode.None &&
                OperatingSystem.IsWindowsVersionAtLeast(10))
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
            ? Color.FromArgb(0xB8, 0x18, 0x18, 0x18)
            : Color.FromArgb(0xC7, 0xF5, 0xF5, 0xF5);

        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == AccentState.EnableAcrylicBlurBehind
                ? 0u
                : 2u,
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
        return DwmSetWindowAttribute(
            Handle,
            attribute,
            ref value,
            sizeof(int)) >= 0;
    }

    private void ApplyThemeColors()
    {
        if (_theme.HighContrast)
        {
            _fallbackSurface = SystemColors.Window;
            _primaryText = SystemColors.WindowText;
            _secondaryText = SystemColors.GrayText;
        }
        else if (_theme.IsDark)
        {
            _fallbackSurface = Color.FromArgb(0x18, 0x18, 0x18);
            _primaryText = Color.FromArgb(0xF5, 0xF5, 0xF5);
            _secondaryText = Color.FromArgb(0xBD, 0xBD, 0xBD);
        }
        else
        {
            _fallbackSurface = Color.FromArgb(0xF5, 0xF5, 0xF5);
            _primaryText = Color.FromArgb(0x1A, 0x1A, 0x1A);
            _secondaryText = Color.FromArgb(0x66, 0x66, 0x66);
        }

        ForeColor = _primaryText;
        _header.ForeColor = _primaryText;
        _lastUpdated.ForeColor = _secondaryText;
        _rows.BackColor = Color.Transparent;

        foreach (Control control in _rows.Controls)
        {
            if (control is Label label)
                label.ForeColor = label.Tag is Color statusColor
                    ? statusColor
                    : _primaryText;
        }
    }

    public void Render(StatusSnapshot snapshot)
    {
        _rows.Controls.Clear();
        _rows.RowStyles.Clear();

        for (int i = 0; i < snapshot.Tools.Count; i++)
        {
            var tool = snapshot.Tools[i];
            _rows.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 26));

            Color dotColor = tool.State switch
            {
                ToolState.Idle => Color.Gray,
                ToolState.Quiet => Color.FromArgb(90, 160, 220),
                ToolState.Active => Color.FromArgb(90, 220, 120),
                _ => Color.Gray,
            };

            _rows.Controls.Add(
                MakeCell(tool.DisplayName, dotColor, bold: true), 0, i);
            _rows.Controls.Add(
                MakeCell(tool.State.ToString(), null), 1, i);
            _rows.Controls.Add(
                MakeCell(tool.State == ToolState.Idle
                    ? "-"
                    : $"{tool.CpuPercent:0}%", null), 2, i);
            _rows.Controls.Add(
                MakeCell(tool.State == ToolState.Idle
                    ? "-"
                    : $"{tool.RamMb:0} MB", null), 3, i);
        }

        _lastUpdated.Text =
            $"Last updated {snapshot.SampledAtUtc.ToLocalTime():HH:mm:ss}";
    }

    private Label MakeCell(
        string text,
        Color? dotColor,
        bool bold = false)
    {
        return new Label
        {
            Text = dotColor.HasValue ? "● " + text : text,
            Dock = DockStyle.Fill,
            ForeColor = dotColor ?? _primaryText,
            BackColor = Color.Transparent,
            Tag = dotColor,
            Font = new Font(
                "Segoe UI",
                9,
                bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
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
        Location = new Point(
            workingArea.Right - Width - 8,
            workingArea.Bottom - Height - 8);

        Show();
        Activate();
    }

    private static ThemeSettings ReadThemeSettings()
    {
        const string path =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        using RegistryKey? key =
            Registry.CurrentUser.OpenSubKey(path);

        // Missing values default to Windows' compatibility default: light.
        bool isLight =
            key?.GetValue("AppsUseLightTheme") is int light
                ? light != 0
                : true;

        bool transparencyEnabled =
            key?.GetValue("EnableTransparency") is not int transparency ||
            transparency != 0;

        return new ThemeSettings(
            IsDark: !isLight,
            TransparencyEnabled: transparencyEnabled,
            HighContrast: SystemInformation.HighContrast);
    }

    private static int ToColorRef(Color color)
    {
        // COLORREF = 0x00BBGGRR
        return color.R | (color.G << 8) | (color.B << 16);
    }

    private static uint ToAbgr(Color color)
    {
        // AccentPolicy GradientColor = 0xAABBGGRR
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

    [DllImport(
        "user32.dll",
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(
        nint hwnd,
        ref WindowCompositionAttribData data);
}
