using System.Reflection;
using System.Windows.Forms;
using AiToolsMonitor.Popup;

namespace AiToolsMonitor.Tests;

public class PopupTests
{
    [Fact]
    public void QuotaProgressBar_IsDecorativeAndCannotReceiveFocus()
    {
        Type progressBarType = typeof(StatusPopup).Assembly.GetType(
            "AiToolsMonitor.Popup.QuotaProgressBar",
            throwOnError: true)!;

        object progressBar = Activator.CreateInstance(progressBarType)!;
        MethodInfo getStyle = progressBarType.GetMethod(
            "GetStyle",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Type controlStylesType = getStyle.GetParameters().Single().ParameterType;
        object selectableStyle = Enum.Parse(controlStylesType, "Selectable");

        try
        {
            Assert.False((bool)progressBarType.GetProperty("TabStop")!.GetValue(progressBar)!);
            Assert.False((bool)getStyle.Invoke(progressBar, [selectableStyle])!);
        }
        finally
        {
            ((IDisposable)progressBar).Dispose();
        }
    }

    [Fact]
    public void StatusPopup_RendersRedesignedCardsAndSavesScreenshot()
    {
        var claudeQuota = new AiToolsMonitor.Monitoring.ToolQuota(
            PrimaryPercent: 42.0,
            SecondaryPercent: 18.0,
            ResetsAt: DateTimeOffset.UtcNow.AddHours(3).AddMinutes(12),
            Freshness: AiToolsMonitor.Monitoring.QuotaFreshness.Live,
            InputTokens: 1240000,
            OutputTokens: 480000,
            CostUsd: 1.45,
            DisplayKind: AiToolsMonitor.Monitoring.QuotaDisplayKind.Percentage,
            PrimaryWindowMinutes: 300,
            SecondaryWindowMinutes: 10080
        );

        var codexQuota = new AiToolsMonitor.Monitoring.ToolQuota(
            PrimaryPercent: 15.0,
            SecondaryPercent: null,
            ResetsAt: DateTimeOffset.UtcNow.AddHours(1).AddMinutes(45),
            Freshness: AiToolsMonitor.Monitoring.QuotaFreshness.Live,
            DisplayKind: AiToolsMonitor.Monitoring.QuotaDisplayKind.Percentage,
            PrimaryWindowMinutes: 300,
            SecondaryWindowMinutes: null
        );

        var tools = new List<AiToolsMonitor.Monitoring.ToolStatus>
        {
            new("Claude Code", AiToolsMonitor.Monitoring.ToolState.Active, 18.4, 1340, 3, claudeQuota),
            new("Hermes Agent", AiToolsMonitor.Monitoring.ToolState.Idle, 0, 0, 0, null),
            new("Codex", AiToolsMonitor.Monitoring.ToolState.Quiet, 0.8, 422, 1, codexQuota),
            new("OpenCode", AiToolsMonitor.Monitoring.ToolState.Idle, 0, 0, 0, null),
            new("Antigravity", AiToolsMonitor.Monitoring.ToolState.Active, 7.2, 788, 2, null)
        };

        var snapshot = new AiToolsMonitor.Monitoring.StatusSnapshot(tools, DateTime.UtcNow);

        using var popup = new StatusPopup();
        popup.ShowNearTray();
        popup.Render(snapshot);
        Application.DoEvents();

        using var bmp = new System.Drawing.Bitmap(popup.Width, popup.Height);
        popup.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, popup.Width, popup.Height));

        string artifactDir = @"C:\Users\shubh\.gemini\antigravity-cli\brain\26789cc0-c6c6-4e14-835a-0ccb759338ca";
        if (!System.IO.Directory.Exists(artifactDir))
        {
            System.IO.Directory.CreateDirectory(artifactDir);
        }
        string artifactPath = System.IO.Path.Combine(artifactDir, "popup_redesign.png");
        bmp.Save(artifactPath, System.Drawing.Imaging.ImageFormat.Png);

        Assert.True(System.IO.File.Exists(artifactPath));
    }
}
