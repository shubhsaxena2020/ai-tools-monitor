using System.Drawing.Drawing2D;

namespace AiToolsMonitor.History.Views;

/// <summary>
/// Usage history window showing date-range summaries, per-tool breakdown,
/// session timeline, and a GitHub-style activity heatmap.
/// Opened from the tray context menu "Usage history..." item.
/// </summary>
public sealed class UsageHistoryForm : Form
{
    private readonly HistoryDatabase _db;
    private readonly Font _headerFont = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _bodyFont = new("Segoe UI", 9f);
    private readonly Font _smallFont = new("Segoe UI", 8f);
    private readonly Font _sectionFont = new("Segoe UI", 9.5f, FontStyle.Bold);
    private readonly Font _tinyFont = new("Segoe UI", 7.5f);

    // Controls
    private readonly Button _btnToday;
    private readonly Button _btnThisWeek;
    private readonly Button _btnThisMonth;
    private readonly Button _btnLast30;
    private readonly DateTimePicker _dtpStart;
    private readonly DateTimePicker _dtpEnd;
    private readonly DataGridView _toolGrid;
    private readonly DataGridView _sessionGrid;
    private readonly HeatmapPanel _heatmap;
    private readonly Label _summaryLabel;
    private readonly SplitContainer _mainSplit;
    private readonly Panel _topPanel;
    private readonly Panel _sessionsSection;

    // Colors matching the app's light theme (see THEME.md)
    private static readonly Color Surface = Color.FromArgb(255, 245, 248);
    private static readonly Color SurfaceAlt = Color.FromArgb(247, 247, 247);
    private static readonly Color Border = Color.FromArgb(229, 229, 229);
    private static readonly Color TextPrimary = Color.FromArgb(26, 26, 26);
    private static readonly Color TextSecondary = Color.FromArgb(95, 95, 95);
    private static readonly Color Accent = Color.FromArgb(235, 75, 130);
    private static readonly Color AccentLight = Color.FromArgb(255, 240, 245);
    private static readonly Color GridBg = Color.White;

    public UsageHistoryForm(HistoryDatabase db)
    {
        _db = db;

        Text = "AI Tools Monitor — Usage History";
        Size = new Size(860, 680);
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Surface;
        Font = _bodyFont;

        // ── Top bar: date-range presets + pickers ──
        _topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(14, 10, 14, 6),
            BackColor = Surface,
        };

        var presetFlow = new FlowLayoutPanel
        {
            Location = new Point(14, 10),
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
        };

        _btnToday = CreatePresetButton("Today");
        _btnThisWeek = CreatePresetButton("This Week");
        _btnThisMonth = CreatePresetButton("This Month");
        _btnLast30 = CreatePresetButton("Last 30 Days");
        presetFlow.Controls.AddRange(new Control[] { _btnToday, _btnThisWeek, _btnThisMonth, _btnLast30 });

        _dtpStart = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Width = 120,
            Location = new Point(14, 44),
            Font = _bodyFont,
        };
        _dtpEnd = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Width = 120,
            Location = new Point(142, 44),
            Font = _bodyFont,
        };

        _summaryLabel = new Label
        {
            Location = new Point(280, 46),
            AutoSize = true,
            Font = _sectionFont,
            ForeColor = Accent,
        };

        _topPanel.Controls.Add(presetFlow);
        _topPanel.Controls.Add(_dtpStart);
        _topPanel.Controls.Add(_dtpEnd);
        _topPanel.Controls.Add(_summaryLabel);

        // Wire up events
        _btnToday.Click += (_, _) => SetPreset(0);
        _btnThisWeek.Click += (_, _) => SetPreset(-7);
        _btnThisMonth.Click += (_, _) => SetPreset(-30);
        _btnLast30.Click += (_, _) => SetPreset(-30);
        _dtpStart.ValueChanged += (_, _) => RefreshData();
        _dtpEnd.ValueChanged += (_, _) => RefreshData();

        // ── Main content: Split top (tool grid + heatmap) / bottom (sessions) ──
        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 280,
            SplitterWidth = 4,
            BackColor = Border,
        };

        // Top panel: tool breakdown table (left) + heatmap (right)
        _toolGrid = CreateDataGridView();
        _toolGrid.Columns.Add("tool", "Tool");
        _toolGrid.Columns.Add("tokens", "Total Tokens");
        _toolGrid.Columns.Add("cost", "Cost (USD)");
        _toolGrid.Columns.Add("sessions", "Sessions");
        _toolGrid.Columns["tool"].Width = 160;
        _toolGrid.Columns["tokens"].Width = 140;
        _toolGrid.Columns["cost"].Width = 100;
        _toolGrid.Columns["sessions"].Width = 80;
        _toolGrid.Columns["tool"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _toolGrid.Columns["tokens"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _toolGrid.Columns["cost"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _toolGrid.Columns["sessions"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        _heatmap = new HeatmapPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };

        // Section labels
        var toolSectionLabel = new Label
        {
            Text = "Per-Tool Breakdown",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 4, 0, 0),
            Font = _sectionFont,
            ForeColor = TextPrimary,
            BackColor = SurfaceAlt,
        };

        var heatmapSectionLabel = new Label
        {
            Text = "Activity Heatmap (90 days)",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 4, 0, 0),
            Font = _sectionFont,
            ForeColor = TextPrimary,
            BackColor = SurfaceAlt,
        };

        var toolPanel = new Panel { Dock = DockStyle.Fill };
        toolPanel.Controls.Add(_toolGrid);
        toolPanel.Controls.Add(toolSectionLabel);

        var heatmapPanel = new Panel { Dock = DockStyle.Fill };
        heatmapPanel.Controls.Add(_heatmap);
        heatmapPanel.Controls.Add(heatmapSectionLabel);

        _mainSplit.Panel1.Controls.Add(toolPanel);
        _mainSplit.Panel1.Controls.Add(heatmapPanel);

        // Bottom panel: session timeline
        _sessionGrid = CreateDataGridView();
        _sessionGrid.Columns.Add("sessionId", "Session ID");
        _sessionGrid.Columns.Add("tool", "Tool");
        _sessionGrid.Columns.Add("start", "Started");
        _sessionGrid.Columns.Add("end", "Ended");
        _sessionGrid.Columns.Add("duration", "Duration");
        _sessionGrid.Columns.Add("tokens", "Total Tokens");
        _sessionGrid.Columns["sessionId"].Width = 280;
        _sessionGrid.Columns["tool"].Width = 110;
        _sessionGrid.Columns["start"].Width = 140;
        _sessionGrid.Columns["end"].Width = 140;
        _sessionGrid.Columns["duration"].Width = 80;
        _sessionGrid.Columns["tokens"].Width = 100;
        _sessionGrid.Columns["tokens"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _sessionGrid.Columns["duration"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        _sessionsSection = new Panel { Dock = DockStyle.Fill };
        _sessionsSection.Controls.Add(_sessionGrid);

        var sessionSectionLabel = new Label
        {
            Text = "Session Timeline",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(8, 4, 0, 0),
            Font = _sectionFont,
            ForeColor = TextPrimary,
            BackColor = SurfaceAlt,
        };
        _sessionsSection.Controls.Add(sessionSectionLabel);

        _mainSplit.Panel2.Controls.Add(_sessionsSection);

        Controls.Add(_mainSplit);
        Controls.Add(_topPanel);

        // Default to this month
        SetPreset(-30);
    }

    private static Button CreatePresetButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(8, 3, 8, 3),
            Cursor = Cursors.Hand,
        };
    }

    private void SetPreset(int daysBack)
    {
        _dtpEnd.Value = DateTime.Today;
        _dtpStart.Value = DateTime.Today.AddDays(daysBack);
    }

    private void RefreshData()
    {
        string start = _dtpStart.Value.ToString("yyyy-MM-dd");
        string end = _dtpEnd.Value.ToString("yyyy-MM-dd");

        // 1. Tool breakdown
        var toolData = _db.GetDailyBreakdownByTool(start, end);
        _toolGrid.Rows.Clear();
        long totalTokens = 0;
        double totalCost = 0;
        foreach (var (tool, tokens, cost, sessCount) in toolData)
        {
            _toolGrid.Rows.Add(tool, FormatTokens(tokens), FormatCost(cost), sessCount);
            totalTokens += tokens;
            totalCost += cost;
        }
        _summaryLabel.Text = $"Total: {FormatTokens(totalTokens)} tokens, {FormatCost(totalCost)}";

        // 2. Heatmap
        _heatmap.LoadData(_db.GetDailyActivityForHeatmap(90));

        // 3. Session timeline
        var sessions = _db.GetSessionsForDateRange(start, end);
        _sessionGrid.Rows.Clear();
        foreach (var (sid, tool, startUtc, endUtc, tokens) in sessions)
        {
            string duration = FormatDuration(startUtc, endUtc);
            string startShort = FormatTimestampShort(startUtc);
            string endShort = FormatTimestampShort(endUtc);
            // Truncate session ID for display
            string sidShort = sid.Length > 12 ? sid[..12] + "…" : sid;
            _sessionGrid.Rows.Add(sidShort, tool, startShort, endShort, duration, FormatTokens(tokens));
        }
    }

    private static string FormatTokens(long tokens)
    {
        return tokens switch
        {
            >= 1_000_000 => $"{tokens / 1_000_000d:0.#}M",
            >= 1_000 => $"{tokens / 1_000d:0.#}K",
            _ => tokens.ToString()
        };
    }

    private static string FormatCost(double cost) => $"${cost:0.00}";

    private static string FormatTimestampShort(string iso)
    {
        if (DateTimeOffset.TryParse(iso, out var dto))
            return dto.ToLocalTime().ToString("MMM dd HH:mm");
        return iso;
    }

    private static string FormatDuration(string startUtc, string endUtc)
    {
        if (DateTimeOffset.TryParse(startUtc, out var s) &&
            DateTimeOffset.TryParse(endUtc, out var e))
        {
            var dur = e - s;
            if (dur.TotalHours >= 1)
                return $"{(int)dur.TotalHours}h {dur.Minutes}m";
            if (dur.TotalMinutes >= 1)
                return $"{(int)dur.TotalMinutes}m";
            return $"{(int)dur.TotalSeconds}s";
        }
        return "--";
    }

    private DataGridView CreateDataGridView()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = true,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = GridBg,
            BorderStyle = BorderStyle.None,
            GridColor = Border,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            Font = _bodyFont,
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = SurfaceAlt,
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = SurfaceAlt,
                ForeColor = TextSecondary,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(6, 2, 6, 2),
            },
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 28,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = GridBg,
                ForeColor = TextPrimary,
                SelectionBackColor = AccentLight,
                SelectionForeColor = TextPrimary,
                Padding = new Padding(6, 2, 6, 2),
            },
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        };
        grid.RowTemplate.Height = 26;
        return grid;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerFont.Dispose();
            _bodyFont.Dispose();
            _smallFont.Dispose();
            _sectionFont.Dispose();
            _tinyFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// GitHub-style activity heatmap panel drawn with GDI+.
/// Each cell represents one day, colored by token intensity.
/// Laid out as a grid of weeks (columns) × days-of-week (rows, Mon-Sun).
/// </summary>
internal sealed class HeatmapPanel : Panel
{
    private List<(string date, long tokens)> _data = new();
    private readonly Dictionary<string, long> _tokensByDate = new();
    private long _maxTokens;
    private int _totalDays;

    // Layout
    private const int CellSize = 14;
    private const int CellGap = 3;
    private const int LeftMargin = 30;  // Space for day-of-week labels
    private const int TopMargin = 20;   // Space for month labels
    private const int RightPadding = 10;
    private const int BottomPadding = 10;

    // Green intensity scale (5 levels, GitHub-style)
    private static readonly Color[] IntensityColors = new[]
    {
        Color.FromArgb(235, 240, 245),  // Level 0: almost empty (light gray-blue)
        Color.FromArgb(155, 198, 185),  // Level 1: light green
        Color.FromArgb(98, 165, 142),   // Level 2: medium green
        Color.FromArgb(55, 128, 110),   // Level 3: dark green
        Color.FromArgb(30, 99, 85),     // Level 4: darkest green
    };

    // Dark mode colors
    private static readonly Color[] DarkIntensityColors = new[]
    {
        Color.FromArgb(38, 38, 48),
        Color.FromArgb(22, 90, 70),
        Color.FromArgb(30, 130, 100),
        Color.FromArgb(40, 170, 130),
        Color.FromArgb(50, 210, 160),
    };

    private bool IsDark => SystemColors.Window.R < 128;

    public HeatmapPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void LoadData(List<(string date, long tokens)> data)
    {
        _data = data;
        _tokensByDate.Clear();
        _maxTokens = 0;
        foreach (var (date, tokens) in data)
        {
            _tokensByDate[date] = tokens;
            if (tokens > _maxTokens) _maxTokens = tokens;
        }

        // Recalculate size
        var today = DateTime.UtcNow.Date;
        _totalDays = 90;
        int weeks = (_totalDays + (int)today.DayOfWeek + 6) / 7; // Mon=0 convention
        int widthNeeded = LeftMargin + weeks * (CellSize + CellGap) + RightPadding;
        int heightNeeded = TopMargin + 7 * (CellSize + CellGap) + BottomPadding;
        Width = widthNeeded;
        Height = heightNeeded;
        MinimumSize = new Size(widthNeeded, heightNeeded);

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        if (_tokensByDate.Count == 0)
        {
            using var noDataFont = new Font("Segoe UI", 9f, FontStyle.Italic);
            using var brush = new SolidBrush(SystemColors.GrayText);
            g.DrawString("No data available", noDataFont, brush, new PointF(LeftMargin, TopMargin + 20));
            return;
        }

        var colors = IsDark ? DarkIntensityColors : IntensityColors;
        var textColor = IsDark ? SystemColors.ControlLight : SystemColors.ControlDark;

        var today = DateTime.UtcNow.Date;
        var startDate = today.AddDays(-(_totalDays - 1));

        // Day-of-week labels (Mon, Wed, Fri)
        string[] dayLabels = ["Mon", "", "Wed", "", "Fri", "", "Sun"];
        using var labelFont = new Font("Segoe UI", 7f);
        using var labelBrush = new SolidBrush(textColor);
        for (int day = 0; day < 7; day++)
        {
            if (!string.IsNullOrEmpty(dayLabels[day]))
            {
                var y = TopMargin + day * (CellSize + CellGap);
                g.DrawString(dayLabels[day], labelFont, labelBrush,
                    new PointF(0, y + 1));
            }
        }

        // Month labels
        string[] monthAbbr = ["Jan", "Feb", "Mar", "Apr", "May", "Jun",
                              "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        int lastMonth = -1;
        using var monthFont = new Font("Segoe UI", 7f);
        using var monthBrush = new SolidBrush(textColor);

        // Calculate columns
        int currentDayOfWeek = ((int)startDate.DayOfWeek + 6) % 7; // Mon=0
        int col = 0;
        var currentDate = startDate;

        for (int i = 0; i < _totalDays; i++)
        {
            int row = ((int)currentDate.DayOfWeek + 6) % 7; // Mon=0
            int x = LeftMargin + col * (CellSize + CellGap);
            int y = TopMargin + row * (CellSize + CellGap);

            // Month label at the top of each week's first day
            if (currentDate.Month != lastMonth && currentDate.Day <= 7)
            {
                g.DrawString(monthAbbr[currentDate.Month - 1], monthFont, monthBrush,
                    new PointF(x, 2));
                lastMonth = currentDate.Month;
            }

            // Get intensity level
            string dateKey = currentDate.ToString("yyyy-MM-dd");
            int level = 0;
            if (_tokensByDate.TryGetValue(dateKey, out long tokens) && tokens > 0 && _maxTokens > 0)
            {
                double ratio = (double)tokens / _maxTokens;
                level = ratio switch
                {
                    >= 0.75 => 4,
                    >= 0.50 => 3,
                    >= 0.25 => 2,
                    >= 0.05 => 1,
                    _ => 1, // Has some activity
                };
            }

            var cellRect = new Rectangle(x, y, CellSize, CellSize);
            Color cellColor = colors[level];

            // Draw cell with rounded corners
            using var path = CreateRoundedPath(cellRect, 2);
            using var cellBrush = new SolidBrush(cellColor);
            g.FillPath(cellBrush, path);

            // Tooltip text on hover (just draw subtle date text)
            // For now, we keep it visual-only

            // Move to next day
            currentDate = currentDate.AddDays(1);
            if (currentDate.DayOfWeek == DayOfWeek.Monday)
                col++;
        }

        // Legend
        int legendX = LeftMargin;
        int legendY = TopMargin + 7 * (CellSize + CellGap) + 4;
        g.DrawString("Less", labelFont, labelBrush, new PointF(legendX, legendY + 2));
        legendX += 30;
        for (int i = 0; i < colors.Length; i++)
        {
            using var legBrush = new SolidBrush(colors[i]);
            g.FillRectangle(legBrush, legendX, legendY, CellSize, CellSize);
            legendX += CellSize + 2;
        }
        g.DrawString("More", labelFont, labelBrush, new PointF(legendX + 2, legendY + 2));
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
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
