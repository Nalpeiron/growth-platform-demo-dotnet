using System.Globalization;
using NalpeironGrowthPlatformDemo.Components;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Zenmeter.Consumption.Client;
using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Application.Zentitle;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPriceProviders;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

var builder = WebApplication.CreateBuilder(args);

// Per-developer / per-machine settings (gitignored). Holds the Nalpeiron connection block
// and environment-specific product/catalog overrides, so each environment keeps its own values
// out of source control. In containers config comes from environment variables.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// US application: format dates, numbers and currency as en-US regardless of server locale.
var usCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = usCulture;
CultureInfo.DefaultThreadCurrentUICulture = usCulture;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services
    .AddOptions<NalpeironOptions>()
    .BindConfiguration(NalpeironOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ZentitleOptions>()
    .BindConfiguration(ZentitleOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        options => options.Prices.Values.All(price => price.Price >= 0),
        "Zentitle prices cannot be negative.")
    .ValidateOnStart();

builder.Services
    .AddOptions<DemoProductsOptions>()
    .BindConfiguration(DemoProductsOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        options => options.Items.Count > 0
                   && options.Items.All(product =>
                       !string.IsNullOrWhiteSpace(product.Path)
                       && product.Variants.All(variant =>
                           !string.IsNullOrWhiteSpace(variant.Label)
                           && !string.IsNullOrWhiteSpace(variant.Path)
                           && !string.IsNullOrWhiteSpace(variant.LogoPath))),
        "At least one product with a route path must be configured.")
    .ValidateOnStart();

builder.Services
    .AddOptions<ZenmeterOptions>()
    .BindConfiguration(ZenmeterOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ZenmeterOptions>, ZenmeterOptionsValidator>();

builder.Services
    .AddOptions<BillingOptions>()
    .BindConfiguration(BillingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<BillingOptions>, BillingOptionsValidator>();

// The whole app runs Interactive Server (SignalR circuit). The demo session id lives in the
// browser's localStorage (see BrowserStorage), while server-side stores keep the demo state and
// each button manages its busy/error state in C#.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<BrowserStorage>();
builder.Services.AddMemoryCache();

// Nalpeiron Growth Platform — shared (generic) layer.
builder.Services.AddSingleton<IAccessTokenProvider, AccessTokenProvider>();
builder.Services.AddScoped<GeneratedManagementApiClientOptions>();
builder.Services.AddHttpClient(AccessTokenProvider.HttpClientName,
    httpClient => { httpClient.Timeout = TimeSpan.FromSeconds(30); });
builder.Services.AddHttpClient<IManagementApiClient, ManagementApiClient>((serviceProvider, httpClient) =>
{
    var nalpeiron = serviceProvider.GetRequiredService<IOptions<NalpeironOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(30);
    if (Uri.TryCreate(nalpeiron.ApiUrl, UriKind.Absolute, out var apiUrl))
    {
        httpClient.BaseAddress = apiUrl;
    }
});
builder.Services.AddZenmeterConsumptionClient(builder.Configuration, "Nalpeiron");
builder.Services
    .AddHttpClient<IZentitleManagementApiGeneratedClient, ZentitleManagementApiGeneratedClient>(httpClient =>
    {
        httpClient.Timeout = TimeSpan.FromSeconds(30);
    });
builder.Services
    .AddHttpClient<IZenmeterManagementApiGeneratedClient, ZenmeterManagementApiGeneratedClient>(httpClient =>
    {
        httpClient.Timeout = TimeSpan.FromSeconds(30);
    });
builder.Services.AddScoped<ICustomersClient, CustomersClient>();

// Zentitle Management API slice.
builder.Services.AddScoped<IZentitleManagementClient, ZentitleManagementClient>();
builder.Services.AddScoped<IPricingCatalog, PricingCatalog>();

// Zenmeter Management API slice.
builder.Services.AddScoped<IZenmeterManagementClient, ZenmeterManagementClient>();
builder.Services.AddScoped<IBillingPriceResolver, BillingPriceResolver>();
builder.Services.AddScoped<IBillingPriceCatalog, BillingPriceCatalog>();
builder.Services
    .AddHttpClient<IFastSpringBillingApiClient, FastSpringBillingApiClient>(httpClient =>
    {
        httpClient.Timeout = TimeSpan.FromSeconds(30);
    });
builder.Services.AddScoped<IBillingPriceProvider, StaticBillingPriceProvider>();
builder.Services.AddScoped<StripeBillingClientFactory>();
builder.Services.AddScoped<StripeBillingCustomerService>();
builder.Services.AddScoped<StripeBillingPriceProvider>();
builder.Services.AddScoped<IBillingPriceProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<StripeBillingPriceProvider>());
builder.Services.AddScoped<FastSpringBillingPriceProvider>();
builder.Services.AddScoped<IBillingPriceProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<FastSpringBillingPriceProvider>());
builder.Services.AddScoped<IZenmeterPricingCatalog, ZenmeterPricingCatalog>();

// Elevate demo orchestration.
builder.Services.AddSingleton<IElevateSessionStore, InMemoryElevateSessionStore>();
builder.Services.AddSingleton<ICheckoutRequestGuard, MemoryCacheCheckoutRequestGuard>();
builder.Services.AddScoped<IZentitleBillingProviderRegistry, ZentitleBillingProviderRegistry>();
builder.Services.AddScoped<IZentitleBillingCapabilitiesResolver>(provider =>
    provider.GetRequiredService<IZentitleBillingProviderRegistry>());
builder.Services.AddScoped<IZentitleBillingProvider, DefaultZentitleBillingProvider>();
builder.Services.AddScoped<IZentitleBillingProvider, FastSpringZentitleBillingProvider>();
builder.Services.AddScoped<IZentitleBillingProvider, StripeZentitleBillingProvider>();
builder.Services.AddScoped<ZentitleFastSpringPopupCheckoutContextService>();
builder.Services.AddScoped<ZentitleBillingStatusService>();
builder.Services.AddScoped<IElevateDemo, ElevateDemoService>();
builder.Services.AddSingleton<IZenmeterDemoSessionStore, InMemoryZenmeterDemoSessionStore>();
builder.Services.AddScoped<ZenmeterSubscriptionUserProvisioner>();
builder.Services.AddScoped<IBillingCheckoutService, BillingCheckoutService>();
builder.Services.AddScoped<IBillingCheckoutProvider, NoneBillingCheckoutProvider>();
builder.Services.AddScoped<IBillingCheckoutProvider, StripeBillingCheckoutProvider>();
builder.Services.AddScoped<IBillingCheckoutProvider, FastSpringBillingCheckoutProvider>();
builder.Services.AddScoped<IFastSpringBillingPaymentVerifier, FastSpringBillingPaymentVerifier>();
builder.Services.AddScoped<IStripeBillingPaymentVerifier, StripeBillingPaymentVerifier>();
builder.Services.AddScoped<FastSpringPopupCheckoutContextService>();
builder.Services.AddScoped<IFastSpringSubscriptionUpdater, FastSpringSubscriptionUpdater>();
builder.Services.AddScoped<IBillingCheckoutTopUpStarter, BillingCheckoutTopUpStarter>();
builder.Services.AddScoped<IBillingTopUpPurchaseProvider, NoneBillingTopUpPurchaseProvider>();
builder.Services.AddScoped<IBillingTopUpPurchaseProvider, StripeBillingTopUpPurchaseProvider>();
builder.Services.AddScoped<IBillingTopUpPurchaseProvider, FastSpringBillingTopUpPurchaseProvider>();
builder.Services.AddScoped<ITopUpPurchaseProvider, TopUpPurchaseProvider>();
builder.Services.AddScoped<IZenmeterTopUpPolicy, ZenmeterTopUpPolicy>();
builder.Services.AddScoped<ZenmeterBillingStatusService>();
builder.Services.AddScoped<ZenmeterPurchaseService>();
builder.Services.AddScoped<ZenmeterWorkspaceQuery>();
builder.Services.AddScoped<ZenmeterUsageService>();
builder.Services.AddScoped<ZenmeterTopUpService>();
builder.Services.AddScoped<IZenmeterDemo, ZenmeterDemoFacade>();

var app = builder.Build();

app.UseStaticFiles();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(usCulture),
    SupportedCultures = [usCulture],
    SupportedUICultures = [usCulture]
});

app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok());

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Diagnostics — Development only (they surface tenant/product configuration).
if (app.Environment.IsDevelopment())
{
    var demoApi = app.MapGroup("/api/demo");

    demoApi.MapGet("/config", (
            IOptions<NalpeironOptions> nalpeiron,
            IOptions<ZentitleOptions> zentitle,
            IOptions<ZenmeterOptions> zenmeter) =>
        Results.Ok(new
        {
            nalpeiron.Value.ApiUrl,
            nalpeiron.Value.WebUrl,
            nalpeiron.Value.TenantId,
            ZentitleProductId = zentitle.Value.ProductId,
            ZenmeterBusinessModelId = zenmeter.Value.BusinessModelId,
            Configured = nalpeiron.Value.HasRequiredConfiguration()
                         && zentitle.Value.HasProductConfiguration()
                         && zenmeter.Value.HasProductConfiguration()
        }));

    // Verifies live connectivity (token, headers, productId) and returns the pricing mapped from
    // Zentitle product offerings + edition features.
    demoApi.MapGet("/zentitle/pricing", async (IPricingCatalog catalog, CancellationToken cancellationToken) =>
    {
        try
        {
            var pricing = await catalog.GetPricing(cancellationToken);
            return Results.Ok(pricing);
        }
        catch (Exception ex)
        {
            return Results.Problem(title: "Zentitle pricing fetch failed", detail: ex.Message);
        }
    });

    // Returns Zenmeter pricing read model mapped from the configured business model.
    demoApi.MapGet("/zenmeter/pricing", async (
        IZenmeterPricingCatalog catalog,
        IOptions<BillingOptions> billing,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var pricing = await catalog.GetPricing(
                billing.Value.DefaultBillingSystem,
                cancellationToken);
            return Results.Ok(pricing);
        }
        catch (Exception ex)
        {
            return Results.Problem(title: "Zenmeter pricing fetch failed", detail: ex.Message);
        }
    });
}

app.Run();
