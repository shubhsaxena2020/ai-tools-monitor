using System.Globalization;
using AiToolsMonitor.History;

namespace AiToolsMonitor.Reports;

public sealed class CostReportForm : Form
{
    private readonly HistoryDatabase _historyDb;
    private readonly CurrencyRateService _currencyRates = new();
    private readonly Label _periodLabel = new();
    private readonly Label _totalLabel = new();
    private readonly Label _forecastLabel = new();
    private readonly Label _convertedLabel = new();
    private readonly ComboBox _currencySelector = new();
    private readonly DataGridView _breakdownGrid = CreateGrid();
    private readonly DataGridView _rankingGrid = CreateGrid();
    private double _monthToDateUsd;

    public CostReportForm(HistoryDatabase historyDb)
    {
        _historyDb = historyDb;

        Text = "AI Tools Monitor - Cost report";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        ClientSize = new Size(940, 680);
        Font = new Font("Segoe UI", 9F);

        ConfigureColumns();
        Controls.Add(BuildLayout());

        Load += (_, _) => LoadReport();
        _currencySelector.SelectedIndexChanged += async (_, _) =>
            await UpdateConvertedTotalAsync();
    }

    private Control BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 7,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Text = "Cost report",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };
        _periodLabel.AutoSize = true;
        _periodLabel.ForeColor = SystemColors.GrayText;
        _periodLabel.Margin = new Padding(0, 0, 0, 12);

        var summary = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        ConfigureSummaryLabel(_totalLabel);
        ConfigureSummaryLabel(_forecastLabel);
        ConfigureSummaryLabel(_convertedLabel);
        _currencySelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _currencySelector.Width = 72;
        _currencySelector.Items.AddRange(["EUR", "GBP", "INR"]);
        _currencySelector.SelectedItem = "INR";
        summary.Controls.Add(_totalLabel);
        summary.Controls.Add(_forecastLabel);
        summary.Controls.Add(new Label
        {
            Text = "Convert to",
            AutoSize = true,
            Margin = new Padding(20, 7, 5, 0),
        });
        summary.Controls.Add(_currencySelector);
        summary.Controls.Add(_convertedLabel);

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(_periodLabel, 0, 1);
        layout.Controls.Add(summary, 0, 2);
        layout.Controls.Add(_breakdownGrid, 0, 3);
        layout.Controls.Add(SectionLabel("Cost-effectiveness ranking"), 0, 4);
        layout.Controls.Add(_rankingGrid, 0, 5);
        layout.Controls.Add(new Label
        {
            Text = "Rates: hardcoded USD model pricing; currency conversion: Frankfurter (24-hour cache).",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 10, 0, 0),
        }, 0, 6);

        return layout;
    }

    private void ConfigureColumns()
    {
        _breakdownGrid.Columns.Add(TextColumn("Tool", 16));
        _breakdownGrid.Columns.Add(TextColumn("Model", 28));
        _breakdownGrid.Columns.Add(TextColumn("Input tokens", 14));
        _breakdownGrid.Columns.Add(TextColumn("Output tokens", 14));
        _breakdownGrid.Columns.Add(TextColumn("Total tokens", 14));
        _breakdownGrid.Columns.Add(TextColumn("Cost (USD)", 14));

        _rankingGrid.Columns.Add(TextColumn("Rank", 9));
        _rankingGrid.Columns.Add(TextColumn("Model", 41));
        _rankingGrid.Columns.Add(TextColumn("Total tokens", 24));
        _rankingGrid.Columns.Add(TextColumn("USD / 1M tokens", 26));
    }

    private void LoadReport()
    {
        try
        {
            DateTime today = DateTime.UtcNow.Date;
            DateTime monthStart = new(today.Year, today.Month, 1);
            string startDate = monthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string endDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var modelUsage = _historyDb.GetModelUsage(startDate, endDate);
            _monthToDateUsd = modelUsage.Sum(row => row.CostUsd);
            var recentCosts = GetLastSevenDailyCosts(today);
            double forecast = CostForecast.CalculateMonthEnd(
                recentCosts,
                _monthToDateUsd,
                today);

            _periodLabel.Text = $"{today:MMMM yyyy} through {today:dd MMM yyyy} (UTC)";
            _totalLabel.Text = $"Month to date: {FormatUsd(_monthToDateUsd)}";
            _forecastLabel.Text = $"Forecast: {FormatUsd(forecast)}";
            _convertedLabel.Text = "Converted total: loading...";

            PopulateBreakdown(modelUsage);
            PopulateRanking(CostEffectivenessRanking.Rank(modelUsage));
            _ = UpdateConvertedTotalAsync();
        }
        catch
        {
            _periodLabel.Text = "Cost data is currently unavailable.";
            _totalLabel.Text = "Month to date: USD only";
            _forecastLabel.Text = "Forecast: unavailable";
            _convertedLabel.Text = string.Empty;
        }
    }

    private IReadOnlyCollection<double> GetLastSevenDailyCosts(DateTime today)
    {
        DateTime start = today.AddDays(-6);
        var storedCosts = _historyDb.GetDailyModelCosts(
                start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToDictionary(row => row.Date, row => row.CostUsd);

        var costs = new List<double>(7);
        for (int offset = 0; offset < 7; offset++)
        {
            string date = start.AddDays(offset)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            costs.Add(storedCosts.GetValueOrDefault(date));
        }

        return costs;
    }

    private void PopulateBreakdown(IReadOnlyList<ModelUsageSummary> modelUsage)
    {
        _breakdownGrid.Rows.Clear();
        foreach (var row in modelUsage)
        {
            _breakdownGrid.Rows.Add(
                row.Tool,
                row.Model,
                row.InputTokens.ToString("N0", CultureInfo.InvariantCulture),
                row.OutputTokens.ToString("N0", CultureInfo.InvariantCulture),
                row.TotalTokens.ToString("N0", CultureInfo.InvariantCulture),
                row.PricingKnown ? FormatUsd(row.CostUsd) : "Unknown");
        }

        if (modelUsage.Count == 0)
            _breakdownGrid.Rows.Add("No model usage has been ingested yet.");
    }

    private void PopulateRanking(IReadOnlyList<ModelRankingRow> ranking)
    {
        _rankingGrid.Rows.Clear();
        for (int index = 0; index < ranking.Count; index++)
        {
            var row = ranking[index];
            _rankingGrid.Rows.Add(
                index + 1,
                row.Model,
                row.TotalTokens.ToString("N0", CultureInfo.InvariantCulture),
                row.CostPerMillionTokens.HasValue
                    ? FormatUsd(row.CostPerMillionTokens.Value)
                    : "Unknown");
        }

        if (ranking.Count == 0)
            _rankingGrid.Rows.Add(string.Empty, "No models to rank yet.");
    }

    private async Task UpdateConvertedTotalAsync()
    {
        try
        {
            string? currency = _currencySelector.SelectedItem as string;
            if (currency is null)
                return;

            double? rate = await _currencyRates.GetUsdRateAsync(currency);
            if (IsDisposed)
                return;

            _convertedLabel.Text = rate.HasValue
                ? $"{currency}: {_monthToDateUsd * rate.Value:N2}"
                : "USD only (exchange rate unavailable)";
        }
        catch
        {
            if (!IsDisposed)
                _convertedLabel.Text = "USD only (exchange rate unavailable)";
        }
    }

    private static DataGridView CreateGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
    }

    private static DataGridViewTextBoxColumn TextColumn(string name, float fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            HeaderText = name,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.Automatic,
        };
    }

    private static Label SectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Margin = new Padding(0, 12, 0, 5),
        };
    }

    private static void ConfigureSummaryLabel(Label label)
    {
        label.AutoSize = true;
        label.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        label.Margin = new Padding(0, 7, 20, 0);
    }

    private static string FormatUsd(double amount)
    {
        return amount >= 0.01
            ? $"${amount:N2}"
            : $"${amount:N6}";
    }
}
