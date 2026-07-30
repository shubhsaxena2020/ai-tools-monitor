using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiToolsMonitor.Budget;

/// <summary>
/// Persistent budget configuration stored as JSON in %LOCALAPPDATA%\AiToolsMonitor\budget.json.
/// Soft cap triggers a daily tray balloon warning; hard cap triggers a more prominent warning.
/// </summary>
public sealed class BudgetConfig
{
    public double SoftCapUsd { get; set; } = 5.0;
    public double HardCapUsd { get; set; } = 15.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string GetConfigPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiToolsMonitor",
        "budget.json");

    public static BudgetConfig Load(string? path = null)
    {
        path ??= GetConfigPath();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<BudgetConfig>(json, JsonOptions) ?? new BudgetConfig();
            }
        }
        catch
        {
            // Best effort — return defaults if file is corrupt
        }
        return new BudgetConfig();
    }

    public void Save(string? path = null)
    {
        path ??= GetConfigPath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best effort
        }
    }
}