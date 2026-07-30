using System.Drawing.Drawing2D;
using AiToolsMonitor.Analysis;
using AiToolsMonitor.Budget;
using AiToolsMonitor.History;
using AiToolsMonitor.History.Views;
using AiToolsMonitor.Popup;
using AiToolsMonitor.Reports;

namespace AiToolsMonitor.Shell;

public enum ShellPage
{
    Dashboard,
    Analysis,
    CostReport,
    UsageHistory,
    Budget,
    Settings,
}

/// <summary>
/// Primary application window. Existing feature forms are hosted as child controls so their
/// data loading, event wiring, and business logic remain unchanged.
/// </summary>
public sealed class MainShellForm : Form
{
    private const int SidebarWidth = 220;

    private static readonly IReadOnlyDictionary<ShellPage, (string Title, string Subtitle)> PageCopy =
        new Dictionary<ShellPage, (string Title, string Subtitle)>
        {
            [ShellPage.Dashboard] = ("Dashboard", "Live process and quota status"),
            [ShellPage.Analysis] = ("Analysis", "Session health and efficiency"),
            [ShellPage.CostReport] = ("Cost Report", "Model spend, forecast, and value"),
            [ShellPage.UsageHistory] = ("Usage History", "Activity over time"),
            [ShellPage.Budget] = ("Budget", "Daily caps and cost insight"),
            [ShellPage.Settings] = ("Settings", "Appearance and budget summary"),
        };

    private readonly StatusPopup _dashboard;
    private readonly HistoryDatabase _historyDb;
    private readonly BudgetConfig _budgetConfig;
    private readonly CostAnomalyDetector _anomalyDetector;
    private readonly Action _onBudgetSaved;
    private readonly Dictionary<ShellPage, Control> _pages = [];
    private readonly Dictionary<ShellPage, ShellNavigationButton> _navigationButtons = [];

    private readonly Panel _sidebar;
    private readonly Panel _header;
    private readonly Panel _contentHost;
    private readonly Label _brandLabel;
    private readonly Label _brandCaptionLabel;
    private readonly Label _pageTitleLabel;
    private readonly Label _pageSubtitleLabel;
    private readonly Label _sidebarFooterLabel;

    private CheckBox? _darkThemeToggle;
    private Label? _settingsAppearanceTitle;
    private Label? _settingsAppearanceDescription;
    private Label? _settingsBudgetTitle;
    private Label? _settingsBudgetSummary;
    private Label? _settingsBudgetDescription;
    private Button? _windowsColorsButton;
    private GlassCardPanel? _appearanceCard;
    private GlassCardPanel? _budgetCard;
    private Control? _activeControl;
    private ShellPage _selectedPage;
    private ThemeSettings _theme;
    private bool _themeOverridden;
    private bool _updatingThemeToggle;
    private bool _disposing;

    public MainShellForm(
        StatusPopup dashboard,
        HistoryDatabase historyDb,
        BudgetConfig budgetConfig,
        CostAnomalyDetector anomalyDetector,
        Action onBudgetSaved)
    {
        _dashboard = dashboard;
        _historyDb = historyDb;
        _budgetConfig = budgetConfig;
        _anomalyDetector = anomalyDetector;
        _onBudgetSaved = onBudgetSaved;
        _theme = ThemeSettings.Read();

        Text = "AI Tools Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(900, 620);
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = true;
        KeyPreview = true;

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
        };

        _pageTitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            Location = new Point(22, 13),
            UseMnemonic = false,
        };

        _pageSubtitleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            Location = new Point(24, 47),
            UseMnemonic = false,
        };

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            Padding = Padding.Empty,
        };
        _header.Controls.Add(_pageSubtitleLabel);
        _header.Controls.Add(_pageTitleLabel);

        var mainArea = new Panel { Dock = DockStyle.Fill };
        mainArea.Controls.Add(_contentHost);
        mainArea.Controls.Add(_header);

        _brandLabel = new Label
        {
            Text = "AI TOOLS",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 21),
            UseMnemonic = false,
        };

        _brandCaptionLabel = new Label
        {
            Text = "MONITOR",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(26, 52),
            UseMnemonic = false,
        };

        var brandPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
            BackColor = Color.Transparent,
        };
        brandPanel.Controls.Add(_brandCaptionLabel);
        brandPanel.Controls.Add(_brandLabel);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14, 12, 14, 12),
            BackColor = Color.Transparent,
        };

        AddNavigationButton(navigation, ShellPage.Dashboard, "\uE80F");
        AddNavigationButton(navigation, ShellPage.Analysis, "\uE9D9");
        AddNavigationButton(navigation, ShellPage.CostReport, "\uE8C7");
        AddNavigationButton(navigation, ShellPage.UsageHistory, "\uE81C");
        AddNavigationButton(navigation, ShellPage.Budget, "\uE7EF");
        AddNavigationButton(navigation, ShellPage.Settings, "\uE713");

        _sidebarFooterLabel = new Label
        {
            Text = "LIVE TELEMETRY  •  LOCAL",
            Dock = DockStyle.Bottom,
            Height = 42,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            UseMnemonic = false,
        };

        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = SidebarWidth,
        };
        _sidebar.Controls.Add(navigation);
        _sidebar.Controls.Add(_sidebarFooterLabel);
        _sidebar.Controls.Add(brandPanel);

        Controls.Add(mainArea);
        Controls.Add(_sidebar);

        FormClosing += OnShellFormClosing;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Hide();
        };

        ApplyShellTheme();
    }

    public ShellPage SelectedPage => _selectedPage;

    public void ShowPage(ShellPage page)
    {
        RefreshSystemTheme();
        NavigateTo(page);

        if (!Visible)
            Show();

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        BringToFront();
        Activate();
    }

    public void NavigateTo(ShellPage page)
    {
        if (_activeControl is { IsDisposed: false } && _selectedPage == page)
        {
            UpdatePageHeader(page);
            return;
        }

        Control pageControl = GetOrCreatePage(page);

        _contentHost.SuspendLayout();
        if (_activeControl is { IsDisposed: false })
        {
            _activeControl.Hide();
            _contentHost.Controls.Remove(_activeControl);
        }

        _activeControl = pageControl;
        _selectedPage = page;
        pageControl.Dock = DockStyle.Fill;
        _contentHost.Controls.Add(pageControl);
        pageControl.Show();
        pageControl.BringToFront();
        _contentHost.ResumeLayout(true);

        foreach (var (navPage, button) in _navigationButtons)
            button.Selected = navPage == page;

        if (page == ShellPage.Settings)
            RefreshBudgetSummary();

        UpdatePageHeader(page);
    }

    private void AddNavigationButton(
        FlowLayoutPanel navigation,
        ShellPage page,
        string glyph)
    {
        var button = new ShellNavigationButton(PageCopy[page].Title, glyph)
        {
            Width = SidebarWidth - 28,
            Height = 48,
            Margin = new Padding(0, 0, 0, 6),
        };
        button.Click += (_, _) => NavigateTo(page);
        _navigationButtons[page] = button;
        navigation.Controls.Add(button);
    }

    private Control GetOrCreatePage(ShellPage page)
    {
        if (_pages.TryGetValue(page, out Control? existing) && !existing.IsDisposed)
            return existing;

        Control created = page switch
        {
            ShellPage.Dashboard => CreateDashboardPage(),
            ShellPage.Analysis => PrepareFeaturePage(
                new AnalysisForm(new SessionAnalysisEngine(_historyDb))),
            ShellPage.CostReport => PrepareFeaturePage(new CostReportForm(_historyDb)),
            ShellPage.UsageHistory => PrepareFeaturePage(new UsageHistoryForm(_historyDb)),
            ShellPage.Budget => CreateBudgetPage(),
            ShellPage.Settings => CreateSettingsPage(),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
        };

        _pages[page] = created;
        return created;
    }

    private Control CreateDashboardPage()
    {
        PrepareEmbeddedForm(_dashboard);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(_dashboard, 1, 0);
        _dashboard.Show();
        return layout;
    }

    private Control CreateBudgetPage()
    {
        var form = new BudgetEditForm(_budgetConfig, _anomalyDetector);
        form.FormClosed += (_, _) =>
        {
            _pages.Remove(ShellPage.Budget);
            if (form.Saved)
                _onBudgetSaved();

            if (!_disposing && _selectedPage == ShellPage.Budget && IsHandleCreated)
            {
                BeginInvoke(new Action(() => NavigateTo(ShellPage.Budget)));
            }
        };
        return PrepareFeaturePage(form);
    }

    private Control CreateSettingsPage()
    {
        ThemePalette palette = _theme.Palette;
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = palette.FallbackSurface,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 342,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140F));

        _appearanceCard = new GlassCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            BackColor = palette.CardBackground,
            BorderColor = palette.CardBorder,
        };

        _settingsAppearanceTitle = CreateSettingsTitle("Appearance", palette.PrimaryText);
        _settingsAppearanceDescription = new Label
        {
            Text = "Match Windows by default, or override the shell and live dashboard.",
            Font = new Font("Segoe UI", 9F),
            ForeColor = palette.SecondaryText,
            AutoSize = true,
            Location = new Point(23, 53),
            UseMnemonic = false,
        };
        _darkThemeToggle = new CheckBox
        {
            Text = "Use dark shell theme",
            Checked = _theme.IsDark,
            AutoSize = true,
            Location = new Point(22, 91),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = palette.PrimaryText,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
        };
        _darkThemeToggle.CheckedChanged += (_, _) =>
        {
            if (_updatingThemeToggle)
                return;
            _themeOverridden = true;
            _theme = _theme with { IsDark = _darkThemeToggle.Checked };
            ApplyShellTheme();
        };

        _windowsColorsButton = new Button
        {
            Text = "Windows color settings",
            Width = 160,
            Height = 32,
            Location = new Point(22, 86),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        _windowsColorsButton.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "ms-settings:colors")
                {
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Best effort, consistent with the tray settings shortcut.
            }
        };
        _appearanceCard.Resize += (_, _) =>
        {
            if (_windowsColorsButton is not null)
            {
                _windowsColorsButton.Location = new Point(
                    Math.Max(22, _appearanceCard.ClientSize.Width -
                        _windowsColorsButton.Width - 22),
                    86);
            }
        };

        _appearanceCard.Controls.Add(_windowsColorsButton);
        _appearanceCard.Controls.Add(_darkThemeToggle);
        _appearanceCard.Controls.Add(_settingsAppearanceDescription);
        _appearanceCard.Controls.Add(_settingsAppearanceTitle);

        _budgetCard = new GlassCardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            BackColor = palette.CardBackground,
            BorderColor = palette.CardBorder,
        };
        _settingsBudgetTitle = CreateSettingsTitle("Budget caps", palette.PrimaryText);
        _settingsBudgetSummary = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = palette.PinkAccent,
            Location = new Point(23, 56),
            UseMnemonic = false,
        };
        _settingsBudgetDescription = new Label
        {
            Text = "Open Budget in the sidebar to change these daily USD limits.",
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = palette.SecondaryText,
            Location = new Point(23, 88),
            UseMnemonic = false,
        };
        _budgetCard.Controls.Add(_settingsBudgetDescription);
        _budgetCard.Controls.Add(_settingsBudgetSummary);
        _budgetCard.Controls.Add(_settingsBudgetTitle);

        layout.Controls.Add(_appearanceCard, 0, 0);
        layout.Controls.Add(_budgetCard, 0, 2);
        page.Controls.Add(layout);
        RefreshBudgetSummary();
        return page;
    }

    private static Label CreateSettingsTitle(string text, Color color)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = color,
            Location = new Point(22, 20),
            UseMnemonic = false,
        };
    }

    private static Form PrepareEmbeddedForm(Form form)
    {
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.ShowInTaskbar = false;
        form.Dock = DockStyle.Fill;
        form.MinimumSize = Size.Empty;
        form.MaximumSize = Size.Empty;
        return form;
    }

    private Form PrepareFeaturePage(Form form)
    {
        PrepareEmbeddedForm(form);
        ApplyFeatureTheme(form);
        return form;
    }

    private void RefreshBudgetSummary()
    {
        if (_settingsBudgetSummary is not null)
        {
            _settingsBudgetSummary.Text =
                $"Soft cap  ${_budgetConfig.SoftCapUsd:0.00}     Hard cap  ${_budgetConfig.HardCapUsd:0.00}";
        }
    }

    private void UpdatePageHeader(ShellPage page)
    {
        (string title, string subtitle) = PageCopy[page];
        _pageTitleLabel.Text = title;
        _pageSubtitleLabel.Text = subtitle;
    }

    private void ApplyShellTheme()
    {
        ThemePalette palette = _theme.Palette;
        Color cardSurface = Composite(palette.CardBackground, palette.FallbackSurface);

        BackColor = palette.FallbackSurface;
        ForeColor = palette.PrimaryText;
        _sidebar.BackColor = cardSurface;
        _header.BackColor = cardSurface;
        _contentHost.BackColor = palette.FallbackSurface;
        _brandLabel.ForeColor = palette.PinkAccent;
        _brandCaptionLabel.ForeColor = palette.SecondaryText;
        _pageTitleLabel.ForeColor = palette.PrimaryText;
        _pageSubtitleLabel.ForeColor = palette.SecondaryText;
        _sidebarFooterLabel.ForeColor = palette.SecondaryText;

        Color selectedSurface = Composite(palette.CardBorder, palette.FallbackSurface);
        foreach (ShellNavigationButton button in _navigationButtons.Values)
        {
            button.ApplyTheme(
                palette.PrimaryText,
                palette.SecondaryText,
                palette.PinkAccent,
                selectedSurface,
                cardSurface);
        }

        _dashboard.ApplyTheme(_theme);
        foreach (var (page, control) in _pages)
        {
            if (page is not ShellPage.Dashboard and not ShellPage.Settings)
                ApplyFeatureTheme(control);
        }
        ApplySettingsTheme(palette);
        Invalidate(true);
    }

    private void RefreshSystemTheme()
    {
        if (_themeOverridden)
            return;

        ThemeSettings current = ThemeSettings.Read();
        if (current != _theme)
        {
            _theme = current;
            if (_darkThemeToggle is not null)
            {
                _updatingThemeToggle = true;
                _darkThemeToggle.Checked = current.IsDark;
                _updatingThemeToggle = false;
            }
            ApplyShellTheme();
        }
    }

    private void ApplyFeatureTheme(Control control)
    {
        ThemePalette palette = _theme.Palette;
        Color cardSurface = Composite(palette.CardBackground, palette.FallbackSurface);

        switch (control)
        {
            case Form:
                control.BackColor = palette.FallbackSurface;
                control.ForeColor = palette.PrimaryText;
                break;
            case GroupBox:
                control.BackColor = cardSurface;
                control.ForeColor = palette.PrimaryText;
                break;
            case SplitContainer split:
                split.BackColor = palette.CardBorder;
                break;
            case TableLayoutPanel or FlowLayoutPanel:
                if (control.BackColor != Color.Transparent)
                    control.BackColor = palette.FallbackSurface;
                break;
            case Panel when IsNeutralSurface(control.BackColor):
                control.BackColor = cardSurface;
                break;
        }

        if (control is Label label)
        {
            if (IsSecondaryText(label.ForeColor))
                label.ForeColor = palette.SecondaryText;
            else if (IsPrimaryText(label.ForeColor))
                label.ForeColor = palette.PrimaryText;
        }
        else if (control is DataGridView grid)
        {
            grid.BackgroundColor = cardSurface;
            grid.GridColor = palette.CardBorder;
            grid.DefaultCellStyle.BackColor = cardSurface;
            grid.DefaultCellStyle.ForeColor = palette.PrimaryText;
            grid.DefaultCellStyle.SelectionBackColor =
                Composite(palette.CardBorder, palette.FallbackSurface);
            grid.DefaultCellStyle.SelectionForeColor = palette.PrimaryText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = palette.FallbackSurface;
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.FallbackSurface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.SecondaryText;
            grid.EnableHeadersVisualStyles = false;
        }
        else if (control is ListView listView)
        {
            listView.BackColor = cardSurface;
            listView.ForeColor = palette.PrimaryText;
        }
        else if (control is TextBoxBase or ComboBox or DateTimePicker)
        {
            control.BackColor = cardSurface;
            control.ForeColor = palette.PrimaryText;
        }
        else if (control is Button button && IsNeutralSurface(button.BackColor))
        {
            button.BackColor = cardSurface;
            button.ForeColor = palette.PrimaryText;
            button.FlatAppearance.BorderColor = palette.CardBorder;
        }

        foreach (Control child in control.Controls)
            ApplyFeatureTheme(child);
    }

    private static bool IsNeutralSurface(Color color)
    {
        int value = color.ToArgb();
        ThemePalette light = new ThemeSettings(false, true, false).Palette;
        ThemePalette dark = new ThemeSettings(true, true, false).Palette;
        return value == SystemColors.Control.ToArgb() ||
               value == SystemColors.Window.ToArgb() ||
               value == Color.White.ToArgb() ||
               value == Color.FromArgb(245, 246, 248).ToArgb() ||
               value == Color.FromArgb(255, 245, 248).ToArgb() ||
               value == Color.FromArgb(247, 247, 247).ToArgb() ||
               value == light.FallbackSurface.ToArgb() ||
               value == dark.FallbackSurface.ToArgb() ||
               value == Composite(light.CardBackground, light.FallbackSurface).ToArgb() ||
               value == Composite(dark.CardBackground, dark.FallbackSurface).ToArgb();
    }

    private static bool IsPrimaryText(Color color)
    {
        int value = color.ToArgb();
        ThemePalette light = new ThemeSettings(false, true, false).Palette;
        ThemePalette dark = new ThemeSettings(true, true, false).Palette;
        return value == SystemColors.ControlText.ToArgb() ||
               value == SystemColors.WindowText.ToArgb() ||
               value == Color.FromArgb(30, 30, 45).ToArgb() ||
               value == Color.FromArgb(50, 50, 70).ToArgb() ||
               value == Color.FromArgb(26, 26, 26).ToArgb() ||
               value == light.PrimaryText.ToArgb() ||
               value == dark.PrimaryText.ToArgb();
    }

    private static bool IsSecondaryText(Color color)
    {
        int value = color.ToArgb();
        ThemePalette light = new ThemeSettings(false, true, false).Palette;
        ThemePalette dark = new ThemeSettings(true, true, false).Palette;
        return value == SystemColors.GrayText.ToArgb() ||
               value == Color.FromArgb(60, 60, 80).ToArgb() ||
               value == Color.FromArgb(95, 95, 95).ToArgb() ||
               value == light.SecondaryText.ToArgb() ||
               value == dark.SecondaryText.ToArgb();
    }

    private void ApplySettingsTheme(ThemePalette palette)
    {
        if (!_pages.TryGetValue(ShellPage.Settings, out Control? settingsPage))
            return;

        settingsPage.BackColor = palette.FallbackSurface;
        if (_appearanceCard is not null)
        {
            _appearanceCard.BackColor = palette.CardBackground;
            _appearanceCard.BorderColor = palette.CardBorder;
        }
        if (_budgetCard is not null)
        {
            _budgetCard.BackColor = palette.CardBackground;
            _budgetCard.BorderColor = palette.CardBorder;
        }
        if (_darkThemeToggle is not null)
            _darkThemeToggle.ForeColor = palette.PrimaryText;
        if (_settingsAppearanceTitle is not null)
            _settingsAppearanceTitle.ForeColor = palette.PrimaryText;
        if (_settingsAppearanceDescription is not null)
            _settingsAppearanceDescription.ForeColor = palette.SecondaryText;
        if (_settingsBudgetTitle is not null)
            _settingsBudgetTitle.ForeColor = palette.PrimaryText;
        if (_settingsBudgetSummary is not null)
            _settingsBudgetSummary.ForeColor = palette.PinkAccent;
        if (_settingsBudgetDescription is not null)
            _settingsBudgetDescription.ForeColor = palette.SecondaryText;
        if (_windowsColorsButton is not null)
        {
            _windowsColorsButton.ForeColor = palette.PrimaryText;
            _windowsColorsButton.BackColor =
                Composite(palette.CardBackground, palette.FallbackSurface);
            _windowsColorsButton.FlatAppearance.BorderColor = palette.CardBorder;
        }

        settingsPage.Invalidate(true);
    }

    private static Color Composite(Color foreground, Color background)
    {
        if (foreground.A == byte.MaxValue)
            return foreground;

        double alpha = foreground.A / 255d;
        return Color.FromArgb(
            (int)Math.Round(foreground.R * alpha + background.R * (1d - alpha)),
            (int)Math.Round(foreground.G * alpha + background.G * (1d - alpha)),
            (int)Math.Round(foreground.B * alpha + background.B * (1d - alpha)));
    }

    private void OnShellFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_disposing && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposing = true;
            Control[] pages = _pages.Values.Distinct().ToArray();
            _pages.Clear();
            foreach (Control page in pages)
                page.Dispose();
            _activeControl = null;
        }
        base.Dispose(disposing);
    }
}

internal sealed class ShellNavigationButton : Button
{
    private readonly Font _iconFont = new("Segoe MDL2 Assets", 14F);
    private readonly Font _labelFont = new("Segoe UI", 9.5F, FontStyle.Bold);
    private readonly string _glyph;
    private bool _selected;
    private bool _hovered;
    private Color _primaryText;
    private Color _secondaryText;
    private Color _accent;
    private Color _selectedSurface;
    private Color _surface;

    public ShellNavigationButton(string label, string glyph)
    {
        Text = label;
        _glyph = glyph;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            Invalidate();
        }
    }

    public void ApplyTheme(
        Color primaryText,
        Color secondaryText,
        Color accent,
        Color selectedSurface,
        Color surface)
    {
        _primaryText = primaryText;
        _secondaryText = secondaryText;
        _accent = accent;
        _selectedSurface = selectedSurface;
        _surface = surface;
        BackColor = surface;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color background = _selected || _hovered ? _selectedSurface : _surface;
        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        if (_selected)
        {
            using var accentBrush = new SolidBrush(_accent);
            e.Graphics.FillRectangle(accentBrush, 0, 8, 4, Height - 16);
        }

        Color iconColor = _selected ? _accent : _secondaryText;
        TextRenderer.DrawText(
            e.Graphics,
            _glyph,
            _iconFont,
            new Rectangle(18, 0, 28, Height),
            iconColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter |
            TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            _labelFont,
            new Rectangle(58, 0, Width - 68, Height),
            _primaryText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -7, -6));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont.Dispose();
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
