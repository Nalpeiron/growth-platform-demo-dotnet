using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using System.Net;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingCheckoutProviders;

public sealed class FastSpringBillingCheckoutProviderTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(7, null)]
    [InlineData(14, null)]
    [InlineData(30, null)]
    [InlineData(null, true)]
    [InlineData(7, true)]
    [InlineData(14, true)]
    [InlineData(30, true)]
    public async Task CreateCheckout_WithStartMode_CreatesServerSessionWithExplicitTrial(
        int? trialDays, bool? requirePaymentMethod)
    {
        // arrange
        object? payload = null;
        var api = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.CreateSession(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((request, _) => payload = request)
            .ReturnsAsync(new FastSpringApiResponse(HttpStatusCode.OK, """{"id":"fs_session_1"}"""));
        var options = BillingCheckoutTestData.CreateBillingOptions();
        if (requirePaymentMethod is { } require)
        {
            options.FastSpring.ZenmeterTrialRequirePaymentMethod = require;
        }
        var provider = new FastSpringBillingCheckoutProvider(Options.Create(options), api.Object);
        var checkout = BillingCheckoutTestData.CreateCheckout() with
        {
            StartMode = trialDays is null ? ZenmeterSubscriptionStartMode.Paid : ZenmeterSubscriptionStartMode.Trial,
            TrialDays = trialDays
        };

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        Assert.Equal("fs_session_1", result.ProviderCheckoutSessionId);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(checkout.Skus[0], item.GetProperty("product").GetString());
        Assert.Equal(trialDays ?? 0, item.GetProperty("pricing").GetProperty("trial").GetInt32());
        Assert.False(item.GetProperty("pricing").GetProperty("paidTrial").GetBoolean());
        Assert.Equal(trialDays is null || requirePaymentMethod == true,
            item.GetProperty("pricing").GetProperty("paymentCollected").GetBoolean());
        Assert.False(item.GetProperty("pricing").TryGetProperty("price", out _));
        Assert.Equal(checkout.CustomerAccountRefId,
            document.RootElement.GetProperty("tags").GetProperty("customer_ref").GetString());
        var cancelUrl = BillingCheckoutTestData.ParseQuery(new Uri("https://demo.test" + result.RedirectUrl).Query)["cancelUrl"];
        Assert.Equal(trialDays is not null, cancelUrl.Contains("trial=true"));
        api.VerifyAll();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid"}""")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.OK, "<html>Temporarily unavailable</html>")]
    [InlineData(HttpStatusCode.OK, "null")]
    [InlineData(HttpStatusCode.OK, "[]")]
    [InlineData(HttpStatusCode.OK, """{"id":"fs_session_1","error":"invalid"}""")]
    public async Task CreateCheckout_WhenSessionCreationFails_DoesNotReturnPopup(HttpStatusCode status, string body)
    {
        // arrange
        var api = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.CreateSession(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse(status, body));
        var provider = new FastSpringBillingCheckoutProvider(
            Options.Create(BillingCheckoutTestData.CreateBillingOptions()), api.Object);

        // act
        var act = () => provider.CreateCheckout(BillingCheckoutTestData.CreateCheckout(), CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task CreateCheckout_WithTrialAndAddon_RejectsBeforeCallingFastSpring()
    {
        // arrange
        var api = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        var provider = new FastSpringBillingCheckoutProvider(
            Options.Create(BillingCheckoutTestData.CreateBillingOptions()), api.Object);
        var checkout = BillingCheckoutTestData.CreateCheckout(["base", "addon"]) with
        {
            StartMode = ZenmeterSubscriptionStartMode.Trial,
            TrialDays = 14
        };

        // act
        var act = () => provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateCheckout_WithMultipleProducts_ReturnsLocalPopupLauncherUrl()
    {
        // arrange
        var provider = CreateProvider();
        var checkout = BillingCheckoutTestData.CreateCheckout([
            "base-sku",
            "recurring-addon-sku",
            "one-time-addon-sku"
        ]);

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Pending, result.Status);
        var uri = new Uri($"https://demo.test{result.RedirectUrl}");
        Assert.Equal("/elevate/saas/billing/fastspring-popup", uri.AbsolutePath);

        var query = BillingCheckoutTestData.ParseQuery(uri.Query);
        Assert.Equal("session-1", query["sessionId"]);
        Assert.Equal(2, query.Count);
        Assert.Equal(
            "/elevate/saas/fastspring/checkout?sku=base-sku&addonSku=recurring-addon-sku%2Cone-time-addon-sku",
            query["cancelUrl"]);
        Assert.DoesNotContain("alex.morgan", result.RedirectUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acme", result.RedirectUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateCheckout_WhenRequestedSkuIsBlank_Throws()
    {
        // arrange
        var provider = CreateProvider();
        var checkout = BillingCheckoutTestData.CreateCheckout([" "]);

        // act
        var act = () => provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("product SKU", exception.Message);
    }

    [Fact]
    public async Task CreateCheckout_ForTopUp_ReturnsOperationSpecificPopupUrl()
    {
        // arrange
        var provider = CreateProvider();
        var checkout = BillingCheckoutTestData.CreateCheckout(["credits-50k-onetime"]) with
        {
            Purpose = BillingCheckoutPurpose.TopUp,
            OperationId = "topup-1",
            TargetSubscriptionId = "subscription-1"
        };

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        var uri = new Uri($"https://demo.test{result.RedirectUrl}");
        var query = BillingCheckoutTestData.ParseQuery(uri.Query);
        Assert.Equal("topup-1", query["operationId"]);
        Assert.Equal("/elevate/saas/workspace", query["cancelUrl"]);
    }

    private static FastSpringBillingCheckoutProvider CreateProvider()
    {
        var api = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.CreateSession(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse(HttpStatusCode.OK, """{"id":"fs_session_1"}"""));
        return new(Options.Create(BillingCheckoutTestData.CreateBillingOptions()), api.Object);
    }
}
