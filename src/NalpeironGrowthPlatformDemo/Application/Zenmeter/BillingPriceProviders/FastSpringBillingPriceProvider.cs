using System.Globalization;
using System.Net;
using System.Text.Json;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Configuration;
using Polly;
using static NalpeironGrowthPlatformDemo.Application.Zenmeter.JsonElementHelpers;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPriceProviders;

public sealed class FastSpringBillingPriceProvider(
    IFastSpringBillingApiClient fastSpringApiClient,
    ILogger<FastSpringBillingPriceProvider> logger) : IBillingPriceProvider
{
    private const int MaxPages = 100;

    private static readonly TimeSpan[] PriceRequestRetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromSeconds(2)
    ];

    public BillingSystem BillingSystem => BillingSystem.FastSpring;

    // Loads the whole FastSpring price catalogue in one operation (the paginated /products/price
    // list endpoint). Callers fetch this once per screen and reuse it for tiers and add-ons, so
    // selecting an offering does not trigger another FastSpring call. There is intentionally no
    // caching: prices changed in FastSpring appear on the next screen load.
    public async Task<IReadOnlyDictionary<string, BillingPrice>?> TryGetPriceBook(
        CancellationToken cancellationToken) =>
        await LoadCatalog(cancellationToken);

    public async Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        var requestedSkus = skus
            .Where(sku => !string.IsNullOrWhiteSpace(sku))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedSkus.Length == 0)
        {
            return new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase);
        }

        var catalog = await LoadCatalog(cancellationToken);

        var prices = new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var sku in requestedSkus)
        {
            if (catalog.TryGetValue(sku, out var price))
            {
                prices[sku] = price;
            }
            else
            {
                missing.Add(sku);
            }
        }

        if (missing.Count > 0)
        {
            throw BillingPriceException.MissingPrices(BillingSystem, missing);
        }

        return prices;
    }

    private async Task<IReadOnlyDictionary<string, BillingPrice>> LoadCatalog(CancellationToken cancellationToken)
    {
        var catalog = new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase);

        var page = 1;
        for (var pageCount = 0; pageCount < MaxPages; pageCount++)
        {
            using var payload = await GetPricePage(page, cancellationToken);
            var root = payload.RootElement;

            if (TryGetProperty(root, "products", out var products) &&
                products.ValueKind == JsonValueKind.Array)
            {
                foreach (var product in products.EnumerateArray())
                {
                    ThrowIfProductFailed(product, page);
                    AddProduct(catalog, product);
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"FastSpring price response (page {page}) did not contain a products array.");
            }

            var nextPage = NextPage(root);
            if (nextPage is null || nextPage.Value <= page)
            {
                break;
            }

            page = nextPage.Value;
        }

        return catalog;
    }

    private static void ThrowIfProductFailed(JsonElement product, int page)
    {
        var result = FirstStringProperty(product, "result");
        if (!string.Equals(result, "error", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var productPath = FirstStringProperty(product, "product", "path") ?? "(unknown product)";
        var details = FirstStringProperty(product, "error", "message", "details")
                      ?? "FastSpring returned an operation-level product error.";
        throw new InvalidOperationException(
            $"FastSpring price response (page {page}) failed for product '{productPath}': {details}");
    }

    private static void AddProduct(Dictionary<string, BillingPrice> catalog, JsonElement product)
    {
        if (!TryGetProperty(product, "product", out var pathElement) ||
            pathElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var path = pathElement.GetString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var amount = ProductPrice(product);
        if (amount is null)
        {
            return;
        }

        catalog[path] = new BillingPrice(
            path,
            checked((int)Math.Round(amount.Value, 0, MidpointRounding.AwayFromZero)),
            path);
    }

    private async Task<JsonDocument> GetPricePage(
        int page,
        CancellationToken cancellationToken)
    {
        var retryPolicy = CreatePriceRequestRetryPolicy(page);
        try
        {
            return await retryPolicy.ExecuteAsync(
                token => fastSpringApiClient.GetProductPricePage(page, token),
                cancellationToken);
        }
        catch (FastSpringApiRequestException exception) when (exception.StatusCode is not null)
        {
            throw PriceRequestException(page, exception.StatusCode.Value, exception.ResponseBody);
        }
    }

    private AsyncPolicy<JsonDocument> CreatePriceRequestRetryPolicy(int page) =>
        Policy<JsonDocument>
            .Handle<FastSpringApiRequestException>(IsTransientPriceFailure)
            .Or<HttpRequestException>(exception => exception.StatusCode is null)
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                PriceRequestRetryDelays,
                (failure, retryDelay, attempt, _) =>
                    LogPriceRequestRetry(page, failure.Exception, retryDelay, attempt));

    private void LogPriceRequestRetry(
        int page,
        Exception? exception,
        TimeSpan retryDelay,
        int attempt)
    {
        if (exception is FastSpringApiRequestException apiException &&
            apiException.StatusCode is { } statusCode)
        {
            logger.LogWarning(
                "FastSpring price request (page {Page}) failed with status {StatusCode} ({Status}). Retry attempt {Attempt} in {RetryDelayMs} ms. Response: {Response}",
                page,
                (int)statusCode,
                statusCode,
                attempt,
                retryDelay.TotalMilliseconds,
                Truncate(apiException.ResponseBody));
            return;
        }

        logger.LogWarning(
            exception,
            "FastSpring price request (page {Page}) failed. Retry attempt {Attempt} in {RetryDelayMs} ms.",
            page,
            attempt,
            retryDelay.TotalMilliseconds);
    }

    private static bool IsTransientPriceFailure(FastSpringApiRequestException exception) =>
        exception.StatusCode is { } statusCode &&
        (statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
         (int)statusCode >= 500);

    private static HttpRequestException PriceRequestException(
        int page,
        HttpStatusCode statusCode,
        string responseBody) =>
        new(
            $"FastSpring price request (page {page}) failed with status {(int)statusCode} ({statusCode}). " +
            $"Response: {Truncate(responseBody)}",
            inner: null,
            statusCode);

    private static int? NextPage(JsonElement root) =>
        TryGetProperty(root, "nextPage", out var nextPage) &&
        nextPage.ValueKind == JsonValueKind.Number &&
        nextPage.TryGetInt32(out var value)
            ? value
            : null;

    // The list endpoint keys `pricing` by country (e.g. "US"), and each entry carries `currency`
    // and a base `price`. Falls back to the legacy shapes for the single-product endpoint / tests.
    private static decimal? ProductPrice(JsonElement product)
    {
        if (TryGetProperty(product, "pricing", out var pricing) &&
            pricing.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(pricing, FastSpringBillingDefaults.PriceCountry, out var countryPricing) &&
                TryRegionPrice(countryPricing, out var countryAmount))
            {
                return countryAmount;
            }

            foreach (var region in pricing.EnumerateObject())
            {
                if (CurrencyCode(region.Value) == FastSpringBillingDefaults.PriceCurrency &&
                    TryRegionPrice(region.Value, out var regionAmount))
                {
                    return regionAmount;
                }
            }
        }

        return ProductUsdPrice(product);
    }

    private static bool TryRegionPrice(JsonElement region, out decimal amount)
    {
        if (TryGetProperty(region, "price", out var price) && TryDecimal(price, out amount))
        {
            return true;
        }

        return TryPriceAmount(region, out amount);
    }

    private static decimal? ProductUsdPrice(JsonElement product)
    {
        if (TryPriceAmount(product, out var directAmount))
        {
            return directAmount;
        }

        if (TryGetProperty(product, "pricing", out var pricing) ||
            TryGetProperty(product, "price", out pricing) ||
            TryGetProperty(product, "prices", out pricing))
        {
            return FindUsdPrice(pricing);
        }

        return FindUsdPrice(product);
    }

    private static decimal? FindUsdPrice(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var amount = FindUsdPrice(item);
                if (amount is not null)
                {
                    return amount;
                }
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetProperty(element, "USD", out var usd) && TryPriceAmount(usd, out var usdAmount))
        {
            return usdAmount;
        }

        if (CurrencyCode(element) == "USD" && TryPriceAmount(element, out var directAmount))
        {
            return directAmount;
        }

        foreach (var property in element.EnumerateObject())
        {
            var amount = FindUsdPrice(property.Value);
            if (amount is not null)
            {
                return amount;
            }
        }

        return null;
    }

    private static string? CurrencyCode(JsonElement element) =>
        FirstStringProperty(element, "currency", "currencyCode", "currency_code");

    private static bool TryPriceAmount(JsonElement element, out decimal amount)
    {
        foreach (var propertyName in new[]
                 {
                     "unitPrice",
                     "unit_price",
                     "unitPriceValue",
                     "price",
                     "priceValue",
                     "total",
                     "totalValue"
                 })
        {
            if (TryGetProperty(element, propertyName, out var value) &&
                TryDecimal(value, out amount))
            {
                return true;
            }
        }

        amount = 0;
        return false;
    }

    private static bool TryDecimal(JsonElement value, out decimal amount)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out amount))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            return true;
        }

        amount = 0;
        return false;
    }

    private static string? FirstStringProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}