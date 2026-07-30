using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AiToolsMonitor.Budget;

/// <summary>
/// Small dialog to view/edit budget caps and display cost anomaly insight.
/// </summary>
public sealed class BudgetEditForm : Form
{
    private readonly BudgetConfig _config;
    private readonly CostAnomalyDetector _anomalyDetector;
    private readonly TextBox _softCapTextBox;
    private readonly TextBox _hardCapTextBox;
    private readonly Label _anomalyLabel;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;

    public bool Saved { get; private set; }

    public BudgetEditForm(BudgetConfig config, CostAnomalyDetector anomalyDetector)
    {
        _config = config;
        _anomalyDetector = anomalyDetector;

        Text = "AI Tools Monitor — Budget Settings";
        Size = new Size(380, 320);
        MinimumSize = new Size(380, 320);
        MaximumSize = new Size(380, 320);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(245, 246, 248);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(14),
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // soft cap label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // soft cap input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // hard cap label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // hard cap input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // anomaly + buttons

        // Soft cap
        var softCapLabel = new Label
        {
            Text = "Soft cap (USD):",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(30, 30, 45),
        };
        mainLayout.Controls.Add(softCapLabel, 0, 0);

        _softCapTextBox = new TextBox
        {
            Text = _config.SoftCapUsd.ToString("F2", CultureInfo.InvariantCulture),
            Dock = DockStyle.Fill,
            MaxLength = 10,
        };
        mainLayout.Controls.Add(_softCapTextBox, 1, 0);

        // Hard cap
        var hardCapLabel = new Label
        {
            Text = "Hard cap (USD):",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(30, 30, 45),
        };
        mainLayout.Controls.Add(hardCapLabel, 0, 1);

        _hardCapTextBox = new TextBox
        {
            Text = _config.HardCapUsd.ToString("F2", CultureInfo.InvariantCulture),
            Dock = DockStyle.Fill,
            MaxLength = 10,
        };
        mainLayout.Controls.Add(_hardCapTextBox, 1, 1);

        // Anomaly insight section
        var anomalyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
        };

        _anomalyLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 80,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.FromArgb(60, 60, 80),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
        };

        try
        {
            var (isAnomaly, todayCost, mean, stddev, zScore) = _anomalyDetector.Detect();
            if (mean > 0)
            {
                _anomalyLabel.Text = $"Cost insight (14-day rolling):\n" +
                    $"  Today: ${todayCost:F2}   Mean: ${mean:F2}   StdDev: ${stddev:F2}\n" +
                    $"  Z-score: {zScore:F2}" +
                    (isAnomaly ? "  ⚠ Anomalous (>2σ above mean)" : "");
                if (isAnomaly)
                    _anomalyLabel.ForeColor = Color.FromArgb(180, 60, 20);
            }
            else
            {
                _anomalyLabel.Text = "Cost insight: insufficient history for anomaly detection (need 3+ days).";
            }
        }
        catch
        {
            _anomalyLabel.Text = "Cost insight: unavailable.";
        }

        anomalyPanel.Controls.Add(_anomalyLabel);

        // Buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(0, 8, 0, 0),
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            Width = 80,
            Height = 30,
            DialogResult = DialogResult.Cancel,
        };
        _cancelButton.Click += (_, _) => Close();

        _saveButton = new Button
        {
            Text = "Save",
            Width = 80,
            Height = 30,
            Enabled = true,
        };
        _saveButton.Click += OnSave;

        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_saveButton);
        anomalyPanel.Controls.Add(buttonPanel);

        mainLayout.SetColumnSpan(anomalyPanel, 2);
        mainLayout.Controls.Add(anomalyPanel, 0, 4);

        Controls.Add(mainLayout);
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (!double.TryParse(_softCapTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double soft) || soft < 0)
        {
            MessageBox.Show("Soft cap must be a non-negative number.", "Invalid input",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!double.TryParse(_hardCapTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hard) || hard < 0)
        {
            MessageBox.Show("Hard cap must be a non-negative number.", "Invalid input",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (hard < soft)
        {
            MessageBox.Show("Hard cap must be >= soft cap.", "Invalid input",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _config.SoftCapUsd = soft;
        _config.HardCapUsd = hard;
        _config.Save();
        Saved = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}