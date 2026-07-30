using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiToolsMonitor.Monitoring;

namespace AiToolsMonitor.Export;

public static class StatusSnapshotFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToCsv(StatusSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "SampledAtUtc,DisplayName,State,CpuPercent,RamMb,ProcessCount,PrimaryPercent,SecondaryPercent,ResetsAt,Freshness,InputTokens,OutputTokens,CacheTokens,ReasoningTokens,CostUsd,ObservedAt,DisplayKind,PrimaryWindowMinutes,SecondaryWindowMinutes");

        foreach (var tool in snapshot.Tools)
        {
            ToolQuota? quota = tool.Quota;
            string[] fields =
            [
                snapshot.SampledAtUtc.ToString("O", CultureInfo.InvariantCulture),
                tool.DisplayName,
                tool.State.ToString(),
                Format(tool.CpuPercent),
                Format(tool.RamMb),
                Format(tool.ProcessCount),
                Format(quota?.PrimaryPercent),
                Format(quota?.SecondaryPercent),
                Format(quota?.ResetsAt),
                quota?.Freshness.ToString() ?? "",
                Format(quota?.InputTokens),
                Format(quota?.OutputTokens),
                Format(quota?.CacheTokens),
                Format(quota?.ReasoningTokens),
                Format(quota?.CostUsd),
                Format(quota?.ObservedAt),
                quota?.DisplayKind.ToString() ?? "",
                Format(quota?.PrimaryWindowMinutes),
                Format(quota?.SecondaryWindowMinutes),
            ];

            builder.AppendLine(string.Join(",", fields.Select(EscapeCsv)));
        }

        return builder.ToString();
    }

    public static string ToJson(StatusSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') &&
            !value.Contains('\r') && !value.Contains('\n'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string Format(object? value)
    {
        return value switch
        {
            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? "",
        };
    }
}
