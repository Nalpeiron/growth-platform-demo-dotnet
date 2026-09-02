using Microsoft.AspNetCore.Components;
using NalpeironGrowthPlatformDemo.Components;
using NalpeironGrowthPlatformDemo.Configuration;
using ZentitleCheckoutPage = NalpeironGrowthPlatformDemo.Components.Pages.Zentitle.Checkout;
using ZentitlePricingPage = NalpeironGrowthPlatformDemo.Components.Pages.Zentitle.Pricing;
using ZenmeterLandingPage = NalpeironGrowthPlatformDemo.Components.Pages.Zenmeter.Landing;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Components;

public sealed class DemoRoutesTests
{
    [Theory]
    [InlineData(BillingSystem.None, "/elevate/default", "/elevate/default/checkout")]
    [InlineData(BillingSystem.FastSpring, "/elevate/fastspring", "/elevate/fastspring/checkout")]
    [InlineData(BillingSystem.Stripe, "/elevate/stripe", "/elevate/stripe/checkout")]
    public void ZentitlePricingAndCheckoutFor_WithBillingSystem_ReturnsProviderSpecificRoutes(
        BillingSystem billingSystem,
        string pricing,
        string checkout)
    {
        // act
        var pricingRoute = DemoRoutes.ZentitlePricingFor(billingSystem);
        var checkoutRoute = DemoRoutes.ZentitleCheckoutFor(billingSystem);

        // assert
        Assert.Equal(pricing, pricingRoute);
        Assert.Equal(checkout, checkoutRoute);
    }

    [Fact]
    public void ZentitleRoutes_WhenRead_AreProductSpecific()
    {
        // assert
        Assert.Equal("/elevate", DemoRoutes.ZentitlePricing);
        Assert.Equal("/elevate/checkout", DemoRoutes.ZentitleCheckout);
        Assert.Equal("/elevate/billing/fastspring-popup", DemoRoutes.ZentitleFastSpringPopup);
        Assert.Equal("/elevate/billing/return", DemoRoutes.ZentitleBillingReturn);
        Assert.Equal("/elevate/saas", DemoRoutes.ZenmeterPricing);
    }

    [Fact]
    public void ZentitlePages_WhenInspected_DeclareParameterizedAndBareDefaultRoutes()
    {
        // act
        var pricingRoutes = Routes<ZentitlePricingPage>();
        var checkoutRoutes = Routes<ZentitleCheckoutPage>();

        // assert
        Assert.Contains("/elevate", pricingRoutes);
        Assert.Contains("/elevate/{BillingProvider}", pricingRoutes);
        Assert.Contains("/elevate/checkout", checkoutRoutes);
        Assert.Contains("/elevate/{BillingProvider}/checkout", checkoutRoutes);
    }

    [Theory]
    [InlineData("/elevate", "fastspring")]
    [InlineData("/elevate/", "fastspring")]
    [InlineData("/elevate/checkout", "fastspring")]
    [InlineData("/elevate/checkout/", "fastspring")]
    public void ZentitleBillingSystemForRoute_WithBareRouteAndStaleProvider_ReturnsNone(
        string path,
        string staleProvider)
    {
        // act
        var result = DemoRoutes.ZentitleBillingSystemForRoute(path, staleProvider);

        // assert
        Assert.Equal(BillingSystem.None, result);
    }

    [Theory]
    [InlineData("/elevate/fastspring", "fastspring", BillingSystem.FastSpring)]
    [InlineData("/elevate/stripe/checkout", "stripe", BillingSystem.Stripe)]
    [InlineData("/elevate/unknown", "unknown", null)]
    public void ZentitleBillingSystemForRoute_WithParameterizedRoute_ResolvesTheRouteProvider(
        string path,
        string provider,
        BillingSystem? expected)
    {
        // act
        var result = DemoRoutes.ZentitleBillingSystemForRoute(path, provider);

        // assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ZenmeterLandingPage_WhenInspected_HasLiteralRouteNotCapturedByZentitlePricing()
    {
        // act
        var zenmeterRoutes = Routes<ZenmeterLandingPage>();
        var zentitlePricingRoutes = Routes<ZentitlePricingPage>();

        // assert
        Assert.Contains("/elevate/saas", zenmeterRoutes);
        Assert.DoesNotContain("/elevate/saas", zentitlePricingRoutes);
    }

    [Fact]
    public void ZentitleCheckoutFallback_WithOfferingAndReturnUrl_PreservesOfferingAndDropsUnsafeReturnUrl()
    {
        // act
        var fallback = DemoRoutes.ZentitleCheckoutFallback(
            "offering 1",
            "/elevate/fastspring?period=yearly");
        var unsafeFallback = DemoRoutes.ZentitleCheckoutFallback("offering-1", "https://evil.test");

        // assert
        Assert.Equal(
            "/elevate/default/checkout?offeringId=offering%201&returnUrl=%2Felevate%2Ffastspring%3Fperiod%3Dyearly",
            fallback);
        Assert.DoesNotContain("evil.test", unsafeFallback);
    }

    [Fact]
    public void ZentitleTrialCheckoutFor_WithFastSpringOrigin_UsesDefaultCheckoutAndReturnsToFastSpringPricing()
    {
        // act
        var checkout = DemoRoutes.ZentitleTrialCheckoutFor(
            BillingSystem.None,
            "trial-1",
            BillingSystem.FastSpring,
            NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.BillingPeriod.Yearly);

        // assert
        Assert.Equal(
            "/elevate/default/checkout?offeringId=trial-1&returnUrl=%2Felevate%2Ffastspring%3Fperiod%3Dyearly",
            checkout);
    }

    private static IReadOnlyList<string> Routes<TComponent>() =>
        typeof(TComponent)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(route => route.Template)
            .ToArray();
}
