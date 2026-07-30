namespace AiToolsMonitor.Reports;

public sealed record ModelPrice(
    double InputUsdPerMillionTokens,
    double OutputUsdPerMillionTokens);

public sealed record ModelCost(double CostUsd, bool IsKnown);

public static class ModelPricingCatalog
{
    private static readonly IReadOnlyDictionary<string, ModelPrice> Prices =
        new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-sonnet-5"] = new(3.00, 15.00),
            ["claude-opus-5"] = new(5.00, 25.00),
            ["claude-haiku-4-5"] = new(1.00, 5.00),
            ["gpt-5.6"] = new(2.50, 15.00),
            ["gpt-4o"] = new(2.50, 10.00),
            ["deepseek"] = new(0.28, 0.42),
            ["mimo"] = new(0.20, 0.60),
        };

    public static bool TryGetPrice(string? model, out ModelPrice price)
    {
        price = default!;
        if (string.IsNullOrWhiteSpace(model))
            return false;

        string normalized = model.Trim();
        if (Prices.TryGetValue(normalized, out price!))
            return true;

        if (normalized.StartsWith("claude-haiku-4-5-", StringComparison.OrdinalIgnoreCase))
            return Prices.TryGetValue("claude-haiku-4-5", out price!);

        if (normalized.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
            return Prices.TryGetValue("deepseek", out price!);

        if (normalized.Contains("mimo", StringComparison.OrdinalIgnoreCase))
            return Prices.TryGetValue("mimo", out price!);

        return false;
    }

    public static ModelCost CalculateCost(
        string? model,
        long inputTokens,
        long outputTokens)
    {
        if (!TryGetPrice(model, out var price))
            return new ModelCost(0, false);

        double cost =
            (Math.Max(0, inputTokens) / 1_000_000d * price.InputUsdPerMillionTokens) +
            (Math.Max(0, outputTokens) / 1_000_000d * price.OutputUsdPerMillionTokens);
        return new ModelCost(cost, true);
    }
}
