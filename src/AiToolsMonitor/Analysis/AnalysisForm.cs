using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AiToolsMonitor.History;

namespace AiToolsMonitor.Analysis;

public sealed class AnalysisForm : Form
{
    private readonly SessionAnalysisEngine _engine;
    private Label _gradeBadgeLabel = null!;
    private Label _gradeScoreLabel = null!;
    private ListView _healthChecksListView = null!;
    private ListView _categoriesListView = null!;
    private Label _oneShotRateLabel = null!;
    private Label _oneShotDetailLabel = null!;
    private DataGridView _efficiencyGridView = null!;
    private Button _refreshButton = null!;

    public AnalysisForm(SessionAnalysisEngine? engine = null)
    {
        _engine = engine ?? new SessionAnalysisEngine(new HistoryDatabase());
        InitializeComponent();
        LoadReport();
    }

    private void InitializeComponent()
    {
        Text = "AI Tools Monitor — Analysis & Comparison";
        Size = new Size(840, 680);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(245, 246, 248);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F)); // Health Grade Section
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F)); // Categories + OneShot Section
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Efficiency Grid Section
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));  // Bottom Action Bar

        // --- 1. Health Grade Section ---
        var healthPanel = new GroupBox
        {
            Text = "Feature 20: Setup Health Grade",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 45)
        };

        var healthLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        healthLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        healthLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var gradeCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(230, 240, 255),
            BorderStyle = BorderStyle.FixedSingle
        };

        _gradeBadgeLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 60,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 28F, FontStyle.Bold),
            ForeColor = Color.FromArgb(10, 80, 180),
            Text = "—"
        };

        _gradeScoreLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = Color.FromArgb(60, 60, 80),
            Text = "Score: —/100"
        };

        gradeCard.Controls.Add(_gradeScoreLabel);
        gradeCard.Controls.Add(_gradeBadgeLabel);

        _healthChecksListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        _healthChecksListView.Columns.Add("Status", 75);
        _healthChecksListView.Columns.Add("Check", 180);
        _healthChecksListView.Columns.Add("Reason / Recommendation", 380);

        healthLayout.Controls.Add(gradeCard, 0, 0);
        healthLayout.Controls.Add(_healthChecksListView, 1, 0);
        healthPanel.Controls.Add(healthLayout);

        // --- 2. Categories & One-Shot Section ---
        var midLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        midLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
        midLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

        // Categories Box
        var catBox = new GroupBox
        {
            Text = "Feature 15: Task Category Breakdown",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 45)
        };

        _categoriesListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        _categoriesListView.Columns.Add("Category", 120);
        _categoriesListView.Columns.Add("Sessions", 80);
        _categoriesListView.Columns.Add("Share", 120);

        catBox.Controls.Add(_categoriesListView);

        // One-Shot Box
        var oneShotBox = new GroupBox
        {
            Text = "Feature 16: One-Shot Edit Success Rate",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 45)
        };

        var oneShotCard = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        _oneShotRateLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 50,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 24F, FontStyle.Bold),
            ForeColor = Color.FromArgb(16, 124, 65),
            Text = "—%"
        };

        _oneShotDetailLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(50, 50, 70),
            Text = "Calculating..."
        };

        oneShotCard.Controls.Add(_oneShotDetailLabel);
        oneShotCard.Controls.Add(_oneShotRateLabel);
        oneShotBox.Controls.Add(oneShotCard);

        midLayout.Controls.Add(catBox, 0, 0);
        midLayout.Controls.Add(oneShotBox, 1, 0);

        // --- 3. Efficiency Table Section ---
        var effBox = new GroupBox
        {
            Text = "Feature 17: Per-Tool Cost Efficiency Comparison",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 45)
        };

        _efficiencyGridView = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            RowHeadersVisible = false,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };

        _efficiencyGridView.Columns.Add("ToolName", "Tool");
        _efficiencyGridView.Columns.Add("Sessions", "Sessions");
        _efficiencyGridView.Columns.Add("TotalTokens", "Total Tokens");
        _efficiencyGridView.Columns.Add("TokensPerSession", "Tokens/Session");
        _efficiencyGridView.Columns.Add("TotalCost", "Total Cost ($)");
        _efficiencyGridView.Columns.Add("CostPerSession", "Cost/Session ($)");
        _efficiencyGridView.Columns.Add("CostPer100k", "Cost / 100k Tokens ($)");

        effBox.Controls.Add(_efficiencyGridView);

        // --- 4. Bottom Action Bar ---
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0)
        };

        _refreshButton = new Button
        {
            Text = "Refresh Analysis",
            Width = 140,
            Height = 32,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _refreshButton.FlatAppearance.BorderSize = 0;
        _refreshButton.Click += (_, _) => LoadReport();

        bottomPanel.Controls.Add(_refreshButton);

        // Add to main layout
        mainLayout.Controls.Add(healthPanel, 0, 0);
        mainLayout.Controls.Add(midLayout, 0, 1);
        mainLayout.Controls.Add(effBox, 0, 2);
        mainLayout.Controls.Add(bottomPanel, 0, 3);

        Controls.Add(mainLayout);
    }

    private void LoadReport()
    {
        try
        {
            var report = _engine.GenerateReport();

            // 1. Health Grade
            _gradeBadgeLabel.Text = report.HealthGrade.Grade;
            _gradeScoreLabel.Text = $"Score: {report.HealthGrade.Score}/100";

            Color gradeColor = report.HealthGrade.Grade switch
            {
                "A" => Color.FromArgb(16, 124, 65),
                "B" => Color.FromArgb(0, 120, 215),
                "C" => Color.FromArgb(200, 140, 0),
                "D" => Color.FromArgb(210, 90, 0),
                _ => Color.FromArgb(190, 30, 30)
            };
            _gradeBadgeLabel.ForeColor = gradeColor;

            _healthChecksListView.Items.Clear();
            foreach (var check in report.HealthGrade.Checks)
            {
                var item = new ListViewItem(check.Status.ToString());
                item.SubItems.Add(check.Name);
                item.SubItems.Add(check.Reason);

                item.ForeColor = check.Status switch
                {
                    HealthStatus.Pass => Color.FromArgb(16, 124, 65),
                    HealthStatus.Warning => Color.FromArgb(180, 100, 0),
                    HealthStatus.Fail => Color.FromArgb(190, 30, 30),
                    _ => Color.Black
                };
                _healthChecksListView.Items.Add(item);
            }

            // 2. Categories
            _categoriesListView.Items.Clear();
            foreach (var cat in report.Categories)
            {
                var item = new ListViewItem(cat.Category);
                item.SubItems.Add(cat.SessionCount.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add($"{cat.Percentage:0.0}%");
                _categoriesListView.Items.Add(item);
            }

            // 3. One-Shot Success
            _oneShotRateLabel.Text = $"{report.OneShotMetrics.SuccessRatePercentage:0.#}%";
            _oneShotDetailLabel.Text = report.OneShotMetrics.TotalEdits > 0
                ? $"{report.OneShotMetrics.OneShotEdits} / {report.OneShotMetrics.TotalEdits} edits succeeded on 1st try\n({report.OneShotMetrics.RetryEdits} retries across {report.OneShotMetrics.EvaluatedSessionsCount} sessions)"
                : "No file edits detected in evaluated sessions.";

            // 4. Tool Efficiency
            _efficiencyGridView.Rows.Clear();
            foreach (var tool in report.ToolEfficiencies)
            {
                _efficiencyGridView.Rows.Add(
                    tool.ToolName,
                    tool.SessionCount > 0 ? tool.SessionCount.ToString(CultureInfo.InvariantCulture) : "—",
                    tool.TotalTokens > 0 ? tool.TotalTokens.ToString("N0", CultureInfo.InvariantCulture) : "0",
                    tool.TokensPerSession > 0 ? tool.TokensPerSession.ToString("N0", CultureInfo.InvariantCulture) : "—",
                    tool.TotalCostUsd > 0 ? $"${tool.TotalCostUsd:0.0000}" : (tool.SessionCount > 0 ? "$0.00" : "—"),
                    tool.CostPerSession > 0 ? $"${tool.CostPerSession:0.0000}" : (tool.SessionCount > 0 ? "$0.00" : "—"),
                    tool.CostPer100kTokens > 0 ? $"${tool.CostPer100kTokens:0.0000}" : (tool.TotalTokens > 0 ? "$0.00" : "—")
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error generating analysis report: {ex.Message}",
                "Analysis Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
