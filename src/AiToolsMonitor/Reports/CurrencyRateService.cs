using System.Text.Json;

namespace AiToolsMonitor.Reports;

public sealed class CurrencyRateService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private readonly HttpClient _httpClient;
    private readonly string _cachePath;
    private readonly Func<DateTimeOffset> _utcNow;
    private RateCache? _memoryCache;

    public CurrencyRateService(
        HttpClient? httpClient = null,
        string? cachePath = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiToolsMonitor",
            "exchange-rates.json");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<double?> GetUsdRateAsync(
        string currency,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
                return 1;

            var cache = _memoryCache ?? await ReadCacheAsync(cancellationToken);
            if (IsFresh(cache) &&
                cache!.Rates.TryGetValue(currency, out double cachedRate))
            {
                _memoryCache = cache;
                return cachedRate;
            }

            using var response = await _httpClient.GetAsync(
                "https://api.frankfurter.app/latest?from=USD",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("rates", out var ratesElement) ||
                ratesElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in ratesElement.EnumerateObject())
            {
                if (property.Value.TryGetDouble(out double rate))
                    rates[property.Name] = rate;
            }

            var freshCache = new RateCache(_utcNow(), rates);
            _memoryCache = freshCache;
            await WriteCacheAsync(freshCache, cancellationToken);

            return rates.TryGetValue(currency, out double liveRate)
                ? liveRate
                : null;
        }
        catch
        {
            return null;
        }
    }

    private bool IsFresh(RateCache? cache)
    {
        return cache is not null &&
               _utcNow() - cache.FetchedAtUtc >= TimeSpan.Zero &&
               _utcNow() - cache.FetchedAtUtc < CacheLifetime;
    }

    private async Task<RateCache?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cachePath))
                return null;

            await using var stream = new FileStream(
                _cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return await JsonSerializer.DeserializeAsync<RateCache>(
                stream,
                cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(
        RateCache cache,
        CancellationToken cancellationToken)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_cachePath);
            if (directory is not null)
                Directory.CreateDirectory(directory);

            await using var stream = new FileStream(
                _cachePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            await JsonSerializer.SerializeAsync(
                stream,
                cache,
                cancellationToken: cancellationToken);
        }
        catch
        {
            // Cache persistence is best effort.
        }
    }

    private sealed record RateCache(
        DateTimeOffset FetchedAtUtc,
        Dictionary<string, double> Rates);
}
