using Microsoft.Extensions.Options;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Zentitle;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zentitle.BillingProviders;

public sealed class FastSpringZentitleBillingProviderTests
{
    [Fact]
    public void Capabilities_WhenRead_SupportOnlyYearlyExternalCheckout()
    {
        // arrange
        var provider = Provider();

        // assert
        Assert.Equal(BillingSystem.FastSpring, provider.BillingSystem);
        Assert.Equal([BillingPeriod.Yearly], provider.Capabilities.SupportedPaidPeriods);
        Assert.False(provider.Capabilities.SupportsPaidPeriod(BillingPeriod.Perpetual));
        Assert.False(provider.Capabilities.SupportsTrialCheckout);
        Assert.False(provider.Capabilities.SupportsUpgrade);
        Assert.True(provider.Capabilities.UsesExternalCheckout);
        Assert.Equal(ZentitlePriceSource.BillingProvider, provider.Capabilities.PriceSource);
    }

    [Fact]
    public async Task CreateCheckout_WithPendingCheckout_ReturnsProductSpecificPopupAndCancelUrls()
    {
        // arrange
        var provider = Provider();

        // act
        var result = await provider.CreateCheckout(PendingCheckout(), CancellationToken.None);

        // assert
        var popup = new Uri($"https://demo.test{result.RedirectUrl}");
        Assert.Equal(ZentitleCheckoutStatuses.Pending, result.Status);
        Assert.Equal("/elevate/billing/fastspring-popup", popup.AbsolutePath);
        var query = BillingCheckoutTestData.ParseQuery(popup.Query);
        Assert.Equal("session-1", query["sessionId"]);
        Assert.Equal(
            "/elevate/fastspring/checkout?offeringId=offering-1",
            query["cancelUrl"]);
    }

    [Fact]
    public async Task CreateCheckout_WithMissingZentitleStorefront_Throws()
    {
        // arrange
        var options = BillingOptions();
        options.FastSpring.ZentitleStorefrontUrl = "";
        var provider = Provider(options);

        // act
        var act = () => provider.CreateCheckout(PendingCheckout(), CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("ZentitleStorefrontUrl", exception.Message);
    }

    [Fact]
    public async Task CreateCheckout_WithMissingOfferingSku_Throws()
    {
        // arrange
        var checkout = PendingCheckout() with { Sku = "" };
        var provider = Provider();

        // act
        var act = () => provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("offering SKU", exception.Message);
    }

    [Fact]
    public void ApplyReturn_WithProviderReferences_StoresThemOnTheSession()
    {
        // arrange
        var session = Session();
        var provider = Provider();

        // act
        var result = provider.ApplyReturn(
            session,
            new ZentitleProviderReturnData("provider-order-1", "provider-subscription-1"));

        // assert
        Assert.Null(result.Error);
        Assert.Equal("provider-order-1", session.ProviderOrderRefId);
        Assert.Equal("provider-subscription-1", session.ProviderSubscriptionRefId);
        Assert.Contains(session.Events, candidate => candidate.Contains("provider-order-1"));
        Assert.Contains(session.Events, candidate => candidate.Contains("provider-subscription-1"));
    }

    [Fact]
    public void ApplyReturn_WithConflictingReferences_ReturnsErrorWithoutMutatingTheSession()
    {
        // arrange
        var session = Session();
        session.ProviderSubscriptionRefId = "original-subscription";

        // act
        var result = Provider().ApplyReturn(
            session,
            new ZentitleProviderReturnData("different-order", "different-subscription"));

        // assert
        Assert.Contains("different subscription reference", result.Error);
        Assert.Null(session.ProviderOrderRefId);
        Assert.Equal("original-subscription", session.ProviderSubscriptionRefId);
    }

    [Fact]
    public async Task FindProvisionedGroup_WithProviderOrderRef_LooksUpByCustomerAndProviderOrderRef()
    {
        // arrange
        var group = new Zt.EntitlementGroupModel { Id = "group-1" };
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .Setup(candidate => candidate.LookupGroup(
                "customer-1",
                "provider-order-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        var provider = Provider(BillingOptions(), zentitle.Object);
        var session = Session();
        session.ProviderOrderRefId = "provider-order-1";

        // act
        var result = await provider.FindProvisionedGroup(session, CancellationToken.None);

        // assert
        Assert.Same(group, result);
        zentitle.VerifyAll();
    }

    private static FastSpringZentitleBillingProvider Provider(
        BillingOptions? options = null,
        IZentitleManagementClient? zentitle = null) =>
        new(
            Options.Create(options ?? BillingOptions()),
            zentitle ?? Mock.Of<IZentitleManagementClient>());

    private static BillingOptions BillingOptions() =>
        new()
        {
            EnabledBillingSystems = [BillingSystem.FastSpring],
            FastSpring = new FastSpringBillingOptions
            {
                ZenmeterStorefrontUrl = "store.test/popup-zenmeter",
                ZentitleStorefrontUrl = "store.test/popup-zentitle"
            }
        };

    private static ZentitlePendingCheckout PendingCheckout() =>
        new(
            "session-1",
            "Acme",
            "customer-1",
            "account-ref-1",
            "demo-order-1",
            "offering-1",
            "sku-1");

    private static ElevateSession Session() =>
        new()
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            ProductId = "product-1",
            EditionId = "edition-1",
            Period = BillingPeriod.Yearly,
            Sku = "sku-1",
            BillingSystem = BillingSystem.FastSpring,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            OrderRefId = "demo-order-1",
            CheckoutStatus = ZentitleCheckoutStatuses.Pending
        };
}
