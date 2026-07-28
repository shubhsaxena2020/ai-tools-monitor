using AiToolsMonitor.Monitoring;

namespace AiToolsMonitor.Tray;

/// <summary>Draws the single tray icon (idle outline vs accent-with-badge, per FR-3).</summary>
public static class TrayIconRenderer
{
    public static Icon Render(int runningCount)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var accent = runningCount > 0 ? Color.FromArgb(90, 200, 120) : Color.FromArgb(140, 140, 140);
            using var brush = new SolidBrush(accent);
            g.FillEllipse(brush, 2, 2, 28, 28);

            if (runningCount > 0)
            {
                var label = runningCount > 5 ? "5+" : runningCount.ToString();
                using var font = new Font("Segoe UI", 12, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.White);
                var size = g.MeasureString(label, font);
                g.DrawString(label, font, textBrush, (32 - size.Width) / 2, (32 - size.Height) / 2);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
