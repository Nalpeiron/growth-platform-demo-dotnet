using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Moq;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingTopUpPurchaseProviders;

public sealed class NoneBillingTopUpPurchaseProviderTests
{
    [Fact]
    public async Task Purchase_WithOneTimeAddon_AddsItDirectlyToTheZenmeterSubscription()
    {
        // arrange
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        string? capturedOrderRefId = "unexpected";
        BillingSystem? capturedBillingSystem = BillingSystem.FastSpring;
        zenmeter
            .Setup(client => client.AddAddons(
                "sub-1",
                It.Is<IReadOnlyList<string>>(skus => skus.SequenceEqual(new[] { "credits-50k-onetime" })),
                It.IsAny<string?>(),
                It.IsAny<BillingSystem?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string?, BillingSystem?, CancellationToken>(
                (_, _, orderRefId, billingSystem, _) =>
                {
                    capturedOrderRefId = orderRefId;
                    capturedBillingSystem = billingSystem;
                })
            .Returns(Task.CompletedTask);
        var provider = new NoneBillingTopUpPurchaseProvider(zenmeter.Object);
        var context = CreateContext(BillingSystem.None, ZenmeterRenewalBehavior.OneTime);

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(BillingSystem.None, provider.BillingSystem);
        Assert.Equal(BillingTopUpResults.Success(), result);
        Assert.Null(capturedOrderRefId);
        Assert.Null(capturedBillingSystem);
        zenmeter.VerifyAll();
        Assert.Contains(context.Session.Events, entry => entry.Contains("Added top-up credits-50k-onetime"));
    }

    [Fact]
    public async Task Purchase_WithRecurringAddonWithoutConfirmation_ReturnsConfirmationWithoutAddingAddon()
    {
        // arrange
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        var provider = new NoneBillingTopUpPurchaseProvider(zenmeter.Object);
        var context = CreateContext(BillingSystem.None, ZenmeterRenewalBehavior.RenewsWithSubscription);

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal(new ZenmeterTopUpConfirmation(
            "credits-50k-onetime",
            "50k credits",
            "This recurring add-on will be added to the subscription and charged automatically each subscription period.",
            "Additional recurring charge",
            "$50"), result.Confirmation);
        zenmeter.Verify(
            client => client.AddAddons(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string?>(),
                It.IsAny<BillingSystem?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Purchase_WithRecurringAddonAfterConfirmation_AddsAddonDirectlyToZenmeterSubscription()
    {
        // arrange
        var zenmeter = new Mock<IZenmeterManagementClient>(MockBehavior.Strict);
        string? capturedOrderRefId = "unexpected";
        BillingSystem? capturedBillingSystem = BillingSystem.FastSpring;
        zenmeter
            .Setup(client => client.AddAddons(
                "sub-1",
                It.Is<IReadOnlyList<string>>(skus => skus.SequenceEqual(new[] { "credits-50k-onetime" })),
                It.IsAny<string?>(),
                It.IsAny<BillingSystem?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string?, BillingSystem?, CancellationToken>(
                (_, _, orderRefId, billingSystem, _) =>
                {
                    capturedOrderRefId = orderRefId;
                    capturedBillingSystem = billingSystem;
                })
            .Returns(Task.CompletedTask);
        var provider = new NoneBillingTopUpPurchaseProvider(zenmeter.Object);
        var context = CreateContext(BillingSystem.None, ZenmeterRenewalBehavior.RenewsWithSubscription) with
        {
            AutomaticPaymentConfirmed = true
        };

        // act
        var result = await provider.Purchase(context, CancellationToken.None);

        // assert
        Assert.Equal(BillingTopUpResults.Success(), result);
        Assert.Null(capturedOrderRefId);
        Assert.Null(capturedBillingSystem);
        zenmeter.VerifyAll();
    }

    [Theory]
    [InlineData(ZenmeterRenewalBehavior.OneTime)]
    [InlineData(ZenmeterRenewalBehavior.RenewsWithSubscription)]
    public void CanPurchase_WithAnyRenewalBehavior_AllowsOneTimeAndRecurringAddons(ZenmeterRenewalBehavior renewalBehavior)
    {
        // arrange
        var provider = new NoneBillingTopUpPurchaseProvider(new Mock<IZenmeterManagementClient>().Object);

        // act
        var canPurchase = provider.CanPurchase(CreateAddon(renewalBehavior));

        // assert
        Assert.True(canPurchase);
    }

    private static BillingTopUpPurchaseContext CreateContext(
        BillingSystem billingSystem,
        ZenmeterRenewalBehavior renewalBehavior) =>
        new(
            new ZenmeterDemoSession
            {
                SessionId = "session-1",
                CustomerName = "Acme",
                TierKey = "scale",
                PlanSku = "elevate-saas-scale-monthly",
                Period = ZenmeterOfferingPeriod.Monthly,
                SubscriptionId = "sub-1",
                BillingSystem = billingSystem,
                User = new ZenmeterUserDetails("user-1", "Alex", "Morgan", "alex@acme.test")
            },
            CreateAddon(renewalBehavior),
            ExistingAddonCount: 0);

    private static ZenmeterAddonPricing CreateAddon(ZenmeterRenewalBehavior renewalBehavior) =>
        new(
            "credits-50k-onetime",
            "50k credits",
            "",
            [],
            ZenmeterAddonType.MeterTopUp,
            50_000,
            50,
            "$50",
            renewalBehavior,
            ZenmeterOfferingPeriod.Monthly,
            IsVisible: true,
            SortOrder: 0);

}
