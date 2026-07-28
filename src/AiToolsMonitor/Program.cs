using AiToolsMonitor.Tray;

namespace AiToolsMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Tray-only app: no main window, Application.Run() with no form keeps
        // the message loop alive as long as the tray icon (and its context
        // menu / popup) exist.
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(true, "AiToolsMonitor.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("AI Tools Monitor is already running.", "AI Tools Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var trayHost = new TrayHost();
        Application.Run();
    }
}
