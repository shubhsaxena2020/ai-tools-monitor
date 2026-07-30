using System.Net;
using AiToolsMonitor.History;
using AiToolsMonitor.Reports;

namespace AiToolsMonitor.Tests;

public class CostReportTests
{
    [Fact]
    public void Forecast_AddsRecentDailyAverageForEveryRemainingDayInMonth()
    {
        double forecast = CostForecast.CalculateMonthEnd(
            lastSevenDailyCosts: [1, 2, 3, 4, 5, 6, 7],
            monthToDateCost: 40,
            asOf: new DateTime(2026, 7, 10));

        Assert.Equal(124, forecast, precision: 8);
    }

    [Fact]
    public void Forecast_WithNoRecentData_ReturnsMonthToDateActual()
    {
        double forecast = CostForecast.CalculateMonthEnd(
            lastSevenDailyCosts: [],
            monthToDateCost: 12.34,
            asOf: new DateTime(2026, 7, 10));

        Assert.Equal(12.34, forecast, precision: 8);
    }

    [Fact]
    public async Task CurrencyRates_UsesSuccessfulResponseFromDiskCacheFor24Hours()
    {
        using var temp = new TempDirectory();
        string cachePath = Path.Combine(temp.Path, "exchange-rates.json");
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        int requests = 0;
        using var onlineClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"amount":1.0,"base":"USD","date":"2026-07-30","rates":{"EUR":0.86,"GBP":0.75,"INR":87.4}}"""),
            };
        }));
        var onlineService = new CurrencyRateService(
            onlineClient,
            cachePath,
            () => now);

        Assert.Equal(87.4, await onlineService.GetUsdRateAsync("INR"));

        using var offlineClient = new HttpClient(new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("offline")));
        var cachedService = new CurrencyRateService(
            offlineClient,
            cachePath,
            () => now.AddHours(23));

        Assert.Equal(0.86, await cachedService.GetUsdRateAsync("EUR"));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task CurrencyRates_ReturnsNullWhenRequestFailsWithoutFreshCache()
    {
        using var temp = new TempDirectory();
        using var offlineClient = new HttpClient(new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("offline")));
        var service = new CurrencyRateService(
            offlineClient,
            Path.Combine(temp.Path, "missing-cache.json"),
            () => new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(await service.GetUsdRateAsync("GBP"));
    }

    [Fact]
    public void CostEffectivenessRanking_SortsKnownModelsAscendingAndUnknownLast()
    {
        ModelUsageSummary[] usage =
        [
            new("Claude Code", "expensive", 50, 50, 0.010, true),
            new("Hermes Agent", "cheap", 50, 50, 0.001, true),
            new("OpenCode", "unknown", 50, 50, 0, false),
            new("OpenCode", "cheap", 100, 100, 0.002, true),
        ];

        var ranking = CostEffectivenessRanking.Rank(usage);

        Assert.Equal(["cheap", "expensive", "unknown"],
            ranking.Select(row => row.Model));
        Assert.Equal(10, ranking[0].CostPerMillionTokens);
        Assert.Null(ranking[2].CostPerMillionTokens);
    }

    [Theory]
    [InlineData("claude-sonnet-5")]
    [InlineData("CLAUDE-SONNET-5")]
    [InlineData("claude-haiku-4-5-20251001")]
    [InlineData("ares/deepseek-v4-pro")]
    [InlineData("posiden/mimo-v2.5")]
    public void PricingLookup_RecognizesExactAliasesAndKnownModelFamilies(string model)
    {
        Assert.True(ModelPricingCatalog.TryGetPrice(model, out var price));
        Assert.True(price.InputUsdPerMillionTokens >= 0);
        Assert.True(price.OutputUsdPerMillionTokens > 0);
    }

    [Fact]
    public void PricingLookup_ReturnsUnknownAndZeroCostForUnmappedModel()
    {
        Assert.False(ModelPricingCatalog.TryGetPrice("unmapped-model", out _));

        var result = ModelPricingCatalog.CalculateCost(
            "unmapped-model",
            inputTokens: 1_000_000,
            outputTokens: 1_000_000);

        Assert.False(result.IsKnown);
        Assert.Equal(0, result.CostUsd);
    }

    [Fact]
    public void PricingLookup_CalculatesInputAndOutputCostPerMillionTokens()
    {
        Assert.True(ModelPricingCatalog.TryGetPrice("gpt-4o", out var price));

        var result = ModelPricingCatalog.CalculateCost(
            "gpt-4o",
            inputTokens: 1_000_000,
            outputTokens: 2_000_000);

        Assert.True(result.IsKnown);
        Assert.Equal(
            price.InputUsdPerMillionTokens + (2 * price.OutputUsdPerMillionTokens),
            result.CostUsd,
            precision: 8);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"AiToolsMonitor.CostTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
