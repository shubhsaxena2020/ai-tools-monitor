using AiToolsMonitor.Tray;

namespace AiToolsMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(true, "AiToolsMonitor.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("AI Tools Monitor is already running.", "AI Tools Monitor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var context = new TrayApplicationContext();
        Application.Run(context);
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayHost _trayHost;

    public TrayApplicationContext()
    {
        _trayHost = new TrayHost();
        Application.ApplicationExit += OnApplicationExit;
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        _trayHost.Dispose();
    }
}
