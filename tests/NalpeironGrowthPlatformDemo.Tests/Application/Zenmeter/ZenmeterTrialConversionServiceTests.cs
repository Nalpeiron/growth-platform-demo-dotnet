using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterTrialConversionServiceTests
{
    [Fact]
    public async Task GetStatus_StripeAttemptExpired_ReturnsToTrialAndAllowsNewConfirmedAttempt()
    {
        // arrange
        var test = new Harness(BillingSystem.Stripe);
        test.Session.TrialConversionPending = true;
        test.Session.TrialConversionRequestId = "expired-attempt";
        test.Billing.Setup(x => x.GetPaymentUrl(test.Session, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                test.Session.TrialConversionPending = false;
                test.Session.TrialConversionRequestId = null;
            }).ReturnsAsync((string?)null);

        // act
        var expired = await test.Service.GetStatus("session", CancellationToken.None);
        var retry = await test.Service.Start("session", true, CancellationToken.None);

        // assert
        Assert.False(expired.Pending);
        Assert.False(expired.Completed);
        Assert.True(test.Subscription.StatusInfo.Trial);
        Assert.True(retry.Pending);
        Assert.NotEqual("expired-attempt", test.Session.TrialConversionRequestId);
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_DirectConversionFails_DoesNotSuggestPaymentIsProcessing()
    {
        // arrange
        var test = new Harness(BillingSystem.None);
        test.Zenmeter.Setup(x => x.ConvertToPaid("subscription", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API unavailable"));

        // act
        var result = await test.Service.Start("session", true, CancellationToken.None);

        // assert
        Assert.False(result.Action.Succeeded);
        Assert.False(result.Pending);
        Assert.DoesNotContain("payment", result.Action.Message!, StringComparison.OrdinalIgnoreCase);
        test.Billing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Start_DirectWithoutConfirmation_DoesNotConvertOrCharge()
    {
        // arrange
        var test = new Harness(BillingSystem.None);

        // act
        var result = await test.Service.Start("session", false, CancellationToken.None);

        // assert
        Assert.True(result.Action.Succeeded);
        Assert.NotNull(result.Confirmation);
        Assert.False(test.Session.TrialConversionPending);
        test.Zenmeter.Verify(x => x.ConvertToPaid(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        test.Billing.Verify(x => x.StartConversion(It.IsAny<ZenmeterDemoSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_DirectTrial_ConvertsSameSubscriptionOnce()
    {
        // arrange
        var test = new Harness(BillingSystem.None);
        test.Zenmeter.Setup(x => x.ConvertToPaid("subscription", It.IsAny<CancellationToken>()))
            .Callback(() => test.Subscription.StatusInfo.Trial = false).Returns(Task.CompletedTask);

        // act
        var first = await test.Service.Start("session", true, CancellationToken.None);
        var repeated = await test.Service.Start("session", true, CancellationToken.None);

        // assert
        Assert.True(first.Completed);
        Assert.True(repeated.Completed);
        Assert.Equal("subscription", test.Session.SubscriptionId);
        Assert.Equal("plan", test.Session.PlanSku);
        test.Zenmeter.Verify(x => x.ConvertToPaid("subscription", It.IsAny<CancellationToken>()), Times.Once);
        test.Billing.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(BillingSystem.Stripe)]
    [InlineData(BillingSystem.FastSpring)]
    public async Task Start_ExternalTrial_WaitsForLiveConversionWithoutRepeatingPayment(BillingSystem provider)
    {
        // arrange
        var test = new Harness(provider);

        // act
        var first = await test.Service.Start("session", true, CancellationToken.None);
        var repeated = await test.Service.Start("session", true, CancellationToken.None);
        var unpaid = await test.Service.GetStatus("session", CancellationToken.None);
        test.Subscription.StatusInfo.Trial = false;
        var paid = await test.Service.GetStatus("session", CancellationToken.None);

        // assert
        Assert.True(first.Pending);
        Assert.True(repeated.Pending);
        Assert.True(unpaid.Pending);
        Assert.False(unpaid.Completed);
        Assert.True(paid.Completed);
        Assert.False(test.Session.TrialConversionPending);
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Once);
        test.Zenmeter.Verify(x => x.ConvertToPaid(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_ConcurrentRequests_OnlyStartsOnePayment()
    {
        // arrange
        var test = new Harness(BillingSystem.Stripe);
        test.Billing.Setup(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()))
            .Returns(async () => { await Task.Yield(); return (string?)null; });

        // act
        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => test.Service.Start("session", true, CancellationToken.None)));

        // assert
        Assert.All(results, result => Assert.True(result.Pending));
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_ProviderRejects_AllowsNewAttempt()
    {
        // arrange
        var test = new Harness(BillingSystem.Stripe);
        test.Billing.SetupSequence(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TrialConversionRejectedException("Card declined"))
            .ReturnsAsync((string?)null);

        // act
        var rejected = await test.Service.Start("session", true, CancellationToken.None);
        var firstRequestId = test.Session.TrialConversionRequestId;
        var retry = await test.Service.Start("session", true, CancellationToken.None);

        // assert
        Assert.False(rejected.Action.Succeeded);
        Assert.False(rejected.Pending);
        Assert.True(retry.Pending);
        Assert.NotEqual(firstRequestId, test.Session.TrialConversionRequestId);
    }

    [Fact]
    public async Task Start_ResponseLost_KeepsPendingAndNeverRepeatsCharge()
    {
        // arrange
        var test = new Harness(BillingSystem.Stripe);
        test.Billing.Setup(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection lost after sending request"));

        // act
        var uncertain = await test.Service.Start("session", true, CancellationToken.None);
        var retry = await test.Service.Start("session", true, CancellationToken.None);

        // assert
        Assert.False(uncertain.Action.Succeeded);
        Assert.Contains("payment may still be processing", uncertain.Action.Message);
        Assert.True(uncertain.Pending);
        Assert.True(retry.Pending);
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStatus_InvoiceNeedsAuthentication_ReturnsPaymentLinkWithoutConverting()
    {
        // arrange
        var test = new Harness(BillingSystem.Stripe);
        test.Session.TrialConversionPending = true;
        test.Billing.Setup(x => x.GetPaymentUrl(test.Session, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://invoice.stripe.test/pay");

        // act
        var result = await test.Service.GetStatus("session", CancellationToken.None);

        // assert
        Assert.True(result.Pending);
        Assert.Equal("https://invoice.stripe.test/pay", result.PaymentUrl);
        test.Zenmeter.Verify(x => x.ConvertToPaid(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("missing_subscription")]
    [InlineData("missing_reference")]
    [InlineData("disabled")]
    [InlineData("cancelled")]
    public async Task Start_IneligibleSubscription_DoesNotCharge(string scenario)
    {
        // arrange
        var test = new Harness(BillingSystem.Stripe);
        if (scenario == "missing_subscription") test.Subscription.StatusInfo = null!;
        if (scenario == "missing_reference") test.Session.SubscriptionRefId = null;
        if (scenario == "disabled") test.Subscription.StatusInfo.DisabledAt = DateTimeOffset.UtcNow;
        if (scenario == "cancelled") test.Subscription.StatusInfo.CancelledAt = DateTimeOffset.UtcNow;

        // act
        var result = await test.Service.Start("session", true, CancellationToken.None);

        // assert
        Assert.False(result.Action.Succeeded);
        test.Billing.Verify(x => x.StartConversion(It.IsAny<ZenmeterDemoSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_FastSpringTrial_OpensPortalAndWaitsForProvisioning()
    {
        // arrange
        var test = new Harness(BillingSystem.FastSpring);
        test.Billing.Setup(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://store.test/account/token/auth#/trials");
        test.Billing.Setup(x => x.GetPaymentUrl(test.Session, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://store.test/account/token/auth#/trials");

        // act
        var result = await test.Service.Start("session", false, CancellationToken.None);
        var resumed = await test.Service.GetStatus("session", CancellationToken.None);
        test.Subscription.StatusInfo.Trial = false;
        var paid = await test.Service.GetStatus("session", CancellationToken.None);

        // assert
        Assert.True(result.Pending);
        Assert.Null(result.Confirmation);
        Assert.Equal("https://store.test/account/token/auth#/trials", result.PaymentUrl);
        Assert.True(result.UsesAccountPortal);
        Assert.True(resumed.UsesAccountPortal);
        Assert.Equal(result.PaymentUrl, resumed.PaymentUrl);
        Assert.True(paid.Completed);
        Assert.Null(paid.PaymentUrl);
        Assert.Null(test.Session.TrialConversionPaymentUrl);
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Once);
        test.Zenmeter.Verify(x => x.ConvertToPaid(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatus_FastSpringTrialBeforeConversion_ExplainsPortalWithoutOpeningIt()
    {
        // arrange
        var test = new Harness(BillingSystem.FastSpring);

        // act
        var result = await test.Service.GetStatus("session", CancellationToken.None);

        // assert
        Assert.True(result.UsesAccountPortal);
        Assert.False(result.Pending);
        Assert.Null(result.PaymentUrl);
        test.Billing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Start_FastSpringPortalAbandoned_KeepsTrialAndReusesPortalWithoutAnotherConversion()
    {
        // arrange
        var test = new Harness(BillingSystem.FastSpring);
        const string portal = "https://store.test/account/token/auth#/trials";
        test.Billing.Setup(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>())).ReturnsAsync(portal);
        test.Billing.Setup(x => x.GetPaymentUrl(test.Session, It.IsAny<CancellationToken>())).ReturnsAsync(portal);

        // act
        await test.Service.Start("session", false, CancellationToken.None);
        var returned = await test.Service.GetStatus("session", CancellationToken.None);
        var retried = await test.Service.Start("session", false, CancellationToken.None);

        // assert
        Assert.True(test.Subscription.StatusInfo.Trial);
        Assert.False(returned.Completed);
        Assert.False(retried.Completed);
        Assert.Equal(portal, retried.PaymentUrl);
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Once);
        test.Zenmeter.Verify(x => x.ConvertToPaid(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Start_FastSpringPortalPreparationFails_AllowsRetryWithoutPaymentProcessingMessage()
    {
        // arrange
        var test = new Harness(BillingSystem.FastSpring);
        test.Billing.SetupSequence(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Checkout unavailable"))
            .ReturnsAsync("https://store.test/account/token/auth#/trials");

        // act
        var failed = await test.Service.Start("session", false, CancellationToken.None);
        var retry = await test.Service.Start("session", false, CancellationToken.None);

        // assert
        Assert.False(failed.Action.Succeeded);
        Assert.False(failed.Pending);
        Assert.DoesNotContain("payment may still be processing", failed.Action.Message);
        Assert.True(retry.Pending);
        Assert.NotNull(retry.PaymentUrl);
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Start_StripeTrial_RequiresConsentBeforeChargingAndWaitsForWebhook()
    {
        // arrange
        var test = new Harness(BillingSystem.Stripe);
        test.Billing.Setup(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://invoice.stripe.test/pay");
        test.Billing.Setup(x => x.GetPaymentUrl(test.Session, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://invoice.stripe.test/pay");

        // act
        var consent = await test.Service.Start("session", false, CancellationToken.None);
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Never);
        var first = await test.Service.Start("session", true, CancellationToken.None);
        var resumed = await test.Service.Start("session", false, CancellationToken.None);
        test.Subscription.StatusInfo.Trial = false;
        var paid = await test.Service.GetStatus("session", CancellationToken.None);

        // assert
        Assert.True(first.Pending);
        Assert.NotNull(consent.Confirmation);
        Assert.Null(first.Confirmation);
        Assert.Equal("https://invoice.stripe.test/pay", first.PaymentUrl);
        Assert.Equal(first.PaymentUrl, resumed.PaymentUrl);
        Assert.True(paid.Completed);
        test.Billing.Verify(x => x.StartConversion(test.Session, It.IsAny<CancellationToken>()), Times.Once);
        test.Zenmeter.Verify(x => x.ConvertToPaid(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Harness
    {
        public Mock<IZenmeterManagementClient> Zenmeter { get; } = new(MockBehavior.Strict);
        public Mock<IZenmeterTrialBilling> Billing { get; } = new(MockBehavior.Strict);
        public Zm.SubscriptionModel Subscription { get; } = new() { StatusInfo = new() { Trial = true } };
        public ZenmeterDemoSession Session { get; }
        public ZenmeterTrialConversionService Service { get; }

        public Harness(BillingSystem provider)
        {
            Session = new ZenmeterDemoSession
            {
                SessionId = "session", CustomerName = "Acme", TierKey = "tier", PlanSku = "plan",
                Period = ZenmeterOfferingPeriod.Monthly, SubscriptionId = "subscription",
                SubscriptionRefId = "provider-subscription", BillingSystem = provider
            };
            var store = new InMemoryZenmeterDemoSessionStore();
            store.Save(Session);
            Zenmeter.Setup(x => x.GetSubscription("subscription", It.IsAny<CancellationToken>())).ReturnsAsync(Subscription);
            Billing.Setup(x => x.StartConversion(Session, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
            Billing.Setup(x => x.GetPaymentUrl(Session, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
            Service = new ZenmeterTrialConversionService(Zenmeter.Object, store, Billing.Object,
                Options.Create(new BillingOptions()), NullLogger<ZenmeterTrialConversionService>.Instance);
        }
    }
}
