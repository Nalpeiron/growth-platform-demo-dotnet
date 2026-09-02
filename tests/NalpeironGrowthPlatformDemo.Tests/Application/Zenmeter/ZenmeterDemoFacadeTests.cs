using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Domain;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using Moq;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;
using System.Net;
using Zenmeter.Consumption.Client.Models;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterDemoFacadeTests
{
    [Fact]
    public async Task Purchase_WithPlanAndAddonSkus_CreatesCustomerAndSubscription()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out var customers);

        // act
        var result = await service.Purchase(
            "elevate-saas-scale-monthly",
            "elevate-saas-credits-100k-monthly",
            "Acme",
            "zm-checkout-1",
            CancellationToken.None);

        // assert
        Assert.NotNull(result.SessionId);
        Assert.Null(result.Error);
        Assert.Equal(1, customers.CreateCalls);
        Assert.Equal("customer-1", zenmeter.CustomerId);
        Assert.Equal(
            ["elevate-saas-scale-monthly", "elevate-saas-credits-100k-monthly"],
            zenmeter.Skus);
        Assert.StartsWith(ReferenceId.Prefix, zenmeter.OrderRefId);
        Assert.Equal("sub-1", zenmeter.CreatedUserSubscriptionId);
        Assert.Equal("demo-user", zenmeter.CreatedExternalUserId);
    }

    [Fact]
    public async Task ConsumeFeature_WithAvailableCredits_UpdatesTheWorkspaceUsageSnapshot()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var consumption = new StubZenmeterConsumptionClient
        {
            Result = Consumed(
                "ai-campaign-draft",
                2,
                40,
                MeterBalanceSnapshot("credits",
                    [Bucket(BucketType.Shared, 80, 99920, 100000)]))
        };
        var service = CreateService(zenmeter, out _, consumptionClient: consumption);

        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-2",
            CancellationToken.None);
        var initialWorkspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);
        var listUsersCallsBeforeConsume = zenmeter.ListUsersCalls;
        var getMetersCallsBeforeConsume = zenmeter.GetMetersCalls;

        // act
        var result = await service.ConsumeFeature(purchase.SessionId!, "ai-campaign-draft", 2, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Null(result.Message);
        Assert.NotNull(result.ViewUpdate);
        Assert.Equal(1, consumption.ConsumeCalls);
        Assert.Equal(listUsersCallsBeforeConsume, zenmeter.ListUsersCalls);
        Assert.Equal(getMetersCallsBeforeConsume, zenmeter.GetMetersCalls);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);
        var credits = Assert.Single(workspace!.Meters);
        var updatedWorkspace = ZenmeterWorkspaceUsageUpdater.Apply(initialWorkspace!, result.ViewUpdate!);
        var updatedCredits = Assert.Single(updatedWorkspace.Meters);
        Assert.Equal(credits.Limit, updatedCredits.Limit);
        Assert.Equal(credits.Used, updatedCredits.Used);
        Assert.Equal(credits.Available, updatedCredits.Available);
        Assert.Equal(credits.UsedPercent, updatedCredits.UsedPercent);
        Assert.Equal(credits.ShowTopUp, updatedCredits.ShowTopUp);
        Assert.Equal(Assert.Single(credits.Sources).Used, Assert.Single(updatedCredits.Sources).Used);
        Assert.Equal(80, credits.Used);
        Assert.Equal(99920, credits.Available);
        Assert.Equal(100000, credits.Limit);
        Assert.Equal("demo-user", consumption.ConsumedUserIdentity?.UserRefId);
        var usageFeature = Assert.Single(workspace.UsageFeatures);
        Assert.Equal(12, usageFeature.ConversionRate);
        Assert.Equal("credit", usageFeature.MeterUnitName);
        Assert.Equal("credits", usageFeature.MeterUnitPluralName);
        Assert.Contains(workspace.Events, entry => entry.Contains("Consumed 2 unit(s) of ai-campaign-draft."));
    }

    [Fact]
    public async Task ConsumeFeature_WithSubscriptionAndAddonBuckets_AggregatesTheirUsage()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = SubscriptionWithAddonMeterGrant("sub-1"),
            Meters = AddonMeterGrantMeters()
        };
        var consumption = new StubZenmeterConsumptionClient
        {
            Result = Consumed(
                "ai-campaign-draft",
                1,
                40,
                MeterBalanceSnapshot("credits",
                [
                    Bucket(BucketType.Shared, 25000, 0, 25000),
                    Bucket(
                        BucketType.AddonShared,
                        23455.25m,
                        26544.75m,
                        50000,
                        "zm-sub-addon-1")
                ]))
        };
        var service = CreateService(zenmeter, out _, consumptionClient: consumption);

        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            "elevate-saas-credits-100k-monthly",
            "Acme",
            "zm-checkout-3",
            CancellationToken.None);

        // act
        var result = await service.ConsumeFeature(purchase.SessionId!, "ai-campaign-draft", 1, CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        var credits = Assert.Single(workspace!.Meters);
        Assert.Equal(48455.25m, credits.Used);
        Assert.Equal(26544.75m, credits.Available);
        Assert.Equal(75000, credits.Limit);
        Assert.Contains(credits.Sources, source =>
            source is { Key: "base", Label: "Subscription", Limit: 25000, Used: 25000, Available: 0, HasUsage: true });
        Assert.Contains(credits.Sources, source =>
            source is { Label: "100k credits / month add-on", TermLabel: "Recurring, Monthly", Limit: 50000, Used: 23455.25m, Available: 26544.75m, HasUsage: true });
    }

    [Fact]
    public async Task ConsumeFeature_WithAddonUsageBucket_MapsItToTheMatchingSource()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = SubscriptionWithTwoAddonMeterGrants("sub-1"),
            Meters = TwoAddonMeterGrantMeters()
        };
        var consumption = new StubZenmeterConsumptionClient
        {
            Result = Consumed(
                "ai-campaign-draft",
                1,
                40,
                MeterBalanceSnapshot("credits",
                [
                    Bucket(BucketType.Shared, 500, 0, 500),
                    Bucket(
                        BucketType.AddonShared,
                        500,
                        0,
                        500,
                        "zm-sub-addon-recurring"),
                    Bucket(
                        BucketType.AddonShared,
                        88,
                        412,
                        500,
                        "zm-sub-addon-topup")
                ]))
        };
        var service = CreateService(zenmeter, out _, consumptionClient: consumption);

        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-grouped-addon-usage",
            CancellationToken.None);

        // act
        var result = await service.ConsumeFeature(purchase.SessionId!, "ai-campaign-draft", 1, CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        var credits = Assert.Single(workspace!.Meters);
        Assert.Equal(1088, credits.Used);
        Assert.Equal(412, credits.Available);
        Assert.Equal(1500, credits.Limit);
        Assert.Contains(credits.Sources, source =>
            source is { Key: "addon:zm-sub-addon-recurring", Used: 500, Available: 0, HasUsage: true });
        Assert.Contains(credits.Sources, source =>
            source is { Key: "addon:zm-sub-addon-topup", Used: 88, Available: 412, HasUsage: true });
    }

    [Fact]
    public async Task
        ConsumeFeature_keeps_meter_grant_limit_when_consumption_snapshot_only_contains_depleted_base_bucket()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = SubscriptionWithAddonMeterGrant("sub-1"),
            Meters = AddonMeterGrantMeters()
        };
        var consumption = new StubZenmeterConsumptionClient
        {
            Result = Consumed(
                "ai-campaign-draft",
                1,
                40,
                MeterBalanceSnapshot("credits",
                    [Bucket(BucketType.Shared, 25000, 0, 25000)]))
        };
        var service = CreateService(zenmeter, out _, consumptionClient: consumption);

        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            "elevate-saas-credits-100k-monthly",
            "Acme",
            "zm-checkout-4",
            CancellationToken.None);

        // act
        var result = await service.ConsumeFeature(purchase.SessionId!, "ai-campaign-draft", 1, CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        var credits = Assert.Single(workspace!.Meters);
        Assert.Equal(25000, credits.Used);
        Assert.Equal(50000, credits.Available);
        Assert.Equal(75000, credits.Limit);
        Assert.Contains(credits.Sources, source =>
            source is { Key: "base", Used: 25000, Available: 0, HasUsage: true });
        Assert.Contains(credits.Sources, source =>
            source is { Key: "addon:zm-sub-addon-1", Used: 0, Available: 50000, HasUsage: false });
    }

    [Fact]
    public async Task ConsumeFeature_WhenBalanceIsInsufficient_UpdatesUsageSnapshotFromTheError()
    {
        // arrange
        var snapshot = Snapshot(
            "ai-campaign-draft",
            1,
            12,
            MeterBalanceSnapshot("credits",
            [
                Bucket(BucketType.Shared, 500, 0, 500),
                Bucket(BucketType.AddonShared, 496, 4, 500, "zm-sub-addon-1")
            ]));
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = SubscriptionWithAddonMeterGrant("sub-1"),
            Meters = AddonMeterGrantMeters(baseGrant: 500, addonGrant: 500)
        };
        var consumption = new StubZenmeterConsumptionClient
        {
            Result = new ConsumptionResult
            {
                Consumed = false,
                Consumption = snapshot,
                ConsumptionError = new ZenmeterApiError
                {
                    Details = "Insufficient subscription balance for requested feature consumption"
                }
            }
        };
        var service = CreateService(zenmeter, out _, consumptionClient: consumption);

        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            "elevate-saas-credits-100k-monthly",
            "Acme",
            "zm-checkout-402",
            CancellationToken.None);

        // act
        var result = await service.ConsumeFeature(purchase.SessionId!, "ai-campaign-draft", 1, CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.False(result.Succeeded);
        Assert.Equal("consume_rejected", result.Code);
        Assert.Equal("Insufficient subscription balance for requested feature consumption", result.Message);
        var credits = Assert.Single(workspace!.Meters);
        Assert.Equal(996, credits.Used);
        Assert.Equal(4, credits.Available);
        Assert.Equal(1000, credits.Limit);
        Assert.Contains(credits.Sources, source =>
            source is { Key: "base", Used: 500, Available: 0, HasUsage: true });
        Assert.Contains(credits.Sources, source =>
            source is { Key: "addon:zm-sub-addon-1", Used: 496, Available: 4, HasUsage: true });
    }

    [Fact]
    public async Task Purchase_WithDuplicateCheckoutRequestId_RejectsTheSecondSubmission()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out var customers);

        // act
        var first = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-duplicate",
            CancellationToken.None);
        var second = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-duplicate",
            CancellationToken.None);

        // assert
        Assert.NotNull(first.SessionId);
        Assert.Null(second.SessionId);
        Assert.Contains("already submitted", second.Error);
        Assert.Equal(1, customers.CreateCalls);
    }

    [Fact]
    public async Task GetWorkspace_WithConfiguredUiBaseUrl_BuildsZenmeterDeepLinks()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("zm-sub_123")
        };
        var service = CreateService(
            zenmeter,
            out _,
            webUrl: "https://tenant-name.nalpeiron.io/zentitle/");

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-links",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Equal("https://tenant-name.nalpeiron.io/zenmeter/customers/customer-1", workspace!.CustomerUrl);
        Assert.Equal(
            "https://tenant-name.nalpeiron.io/zenmeter/subscriptions/zm-sub_123",
            workspace.SubscriptionUrl);
    }

    [Fact]
    public async Task GetWorkspace_WithAccessFeatures_ReturnsTheirStatuses()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1"),
            Features = AccessFeatures()
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-access",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Contains(workspace!.AccessFeatures, feature => feature is { Key: "team-workspace", Enabled: true });
        Assert.Contains(workspace.AccessFeatures, feature => feature is { Key: "sso", Enabled: false });
        Assert.DoesNotContain(workspace.UsageFeatures, feature => feature.Key == "team-workspace");
    }

    [Fact]
    public async Task GetWorkspace_WithAddonAccessFeatures_IncludesEnabledFeatures()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = SubscriptionWithAddonAccessFeatures("sub-1"),
            Features = AddonAccessFeatures()
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            "elevate-saas-security-suite-1m",
            "Acme",
            "zm-checkout-addon-access",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Contains(workspace!.AccessFeatures, feature => feature is { Key: "team-workspace", Enabled: true });
        Assert.Contains(workspace.AccessFeatures, feature => feature is { Key: "audit-logs", Enabled: true });
        Assert.Contains(workspace.AccessFeatures, feature => feature is { Key: "sso", Enabled: true });
        var addon = Assert.Single(workspace.ActiveAddons);
        Assert.Equal("Security Suite", addon.Name);
        Assert.Equal("One-time, 1 month", addon.TermLabel);
    }

    [Fact]
    public async Task GetWorkspace_WithProvisionedUser_ExposesTheLoggedInSubscriptionUser()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out _);

        // act
        var user = new ZenmeterUserInput(
            "Alex",
            "Morgan",
            "alex.morgan@acme.test");
        var purchase = await service.Purchase(
            BillingSystem.None,
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            user,
            "zm-checkout-user",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Equal("alex-morgan", workspace!.User.ExternalUserId);
        Assert.Equal("Alex Morgan", workspace.User.DisplayName);
        Assert.Equal("alex.morgan@acme.test", workspace.User.Email);
        Assert.Equal("enabled", workspace.User.Status);
    }

    [Fact]
    public async Task GetWorkspace_WithSubscriptionExpiry_UsesItAsTheNextRenewal()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        zenmeter.Subscription.StatusInfo = new Zm.SubscriptionStatusModel
        {
            Status = Zm.SubscriptionStatus.Active,
            ExpiryDate = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            Trial = false
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-renewal",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), workspace!.NextRenewalAt);
    }

    [Fact]
    public async Task GetWorkspace_WithCreditAddons_IncludesVisibleOneTimeAndAvailableRecurringTopUpOptions()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-topup-options",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Equal(
            [
                "elevate-saas-credits-50k-onetime",
                "elevate-saas-credits-100k-monthly"
            ],
            workspace!.TopUpOptions.Select(option => option.Sku));
        Assert.False(workspace.TopUpOptions[0].IsRecurring);
        Assert.True(workspace.TopUpOptions[1].IsRecurring);
    }

    [Fact]
    public async Task GetWorkspace_WithStripeBilling_HidesRecurringTopUpOptions()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-stripe-topup-options",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            SubscriptionId = "sub-1",
            BillingSystem = BillingSystem.Stripe,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out _, store: store);

        // act
        var workspace = await service.GetWorkspace(
            "session-stripe-topup-options",
            CancellationToken.None);

        // assert
        Assert.Equal(
            ["elevate-saas-credits-50k-onetime"],
            workspace!.TopUpOptions.Select(option => option.Sku));
    }

    [Fact]
    public async Task GetWorkspace_WithRecurringAddonAlreadyOnSubscription_KeepsThatTopUpOption()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = SubscriptionWithAddonMeterGrant("sub-1")
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-existing-recurring-topup",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Equal(
            [
                "elevate-saas-credits-50k-onetime",
                "elevate-saas-credits-100k-monthly"
            ],
            workspace!.TopUpOptions.Select(option => option.Sku));
    }

    [Fact]
    public async Task GetWorkspace_WithOneTimeAddonAlreadyOnSubscription_KeepsThatTopUpOption()
    {
        // arrange
        var subscription = Subscription("sub-1");
        subscription.Addons =
        [
            Addon(
                "zm-sub-addon-onetime",
                "elevate-saas-credits-50k-onetime",
                "50k credits",
                Zm.AddonRenewalBehavior.OneTime,
                duration: new Zm.Interval { Type = Zm.IntervalType.Month, Count = 1 })
        ];
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = subscription
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-existing-onetime-topup",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Equal(
            [
                "elevate-saas-credits-50k-onetime",
                "elevate-saas-credits-100k-monthly"
            ],
            workspace!.TopUpOptions.Select(option => option.Sku));
    }

    [Fact]
    public async Task GetWorkspace_WithCreditUsageAroundTheThreshold_TogglesTheTopUpCta()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var consumption = new StubZenmeterConsumptionClient
        {
            Result = Consumed(
                "ai-campaign-draft",
                2000,
                40,
                MeterBalanceSnapshot("credits",
                    [Bucket(BucketType.Shared, 80000, 20000, 100000)]))
        };
        var service = CreateService(zenmeter, out _, consumptionClient: consumption);

        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-topup-threshold",
            CancellationToken.None);
        var initialWorkspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // act
        var result =
            await service.ConsumeFeature(purchase.SessionId!, "ai-campaign-draft", 2000, CancellationToken.None);
        var highUsageWorkspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.False(Assert.Single(initialWorkspace!.Meters).ShowTopUp);
        var credits = Assert.Single(highUsageWorkspace!.Meters);
        Assert.Equal(80, credits.UsedPercent);
        Assert.True(credits.ShowTopUp);
    }

    [Fact]
    public async Task AddTopUp_WithSelectedAddon_AddsItAndLogsAnEvent()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-topup",
            CancellationToken.None);
        var result = await service.AddTopUp(
            purchase.SessionId!,
            "elevate-saas-credits-50k-onetime",
            CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal("sub-1", zenmeter.AddedAddonSubscriptionId);
        Assert.Equal(["elevate-saas-credits-50k-onetime"], zenmeter.AddedAddonSkus);
        Assert.Contains(workspace!.Events, entry => entry.Contains("Added top-up elevate-saas-credits-50k-onetime"));
    }

    [Fact]
    public async Task AddTopUp_WithRecurringAddonAndNoExternalBilling_AddsTheAddon()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-recurring-topup",
            CancellationToken.None);
        var result = await service.AddTopUp(
            purchase.SessionId!,
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal(new ZenmeterTopUpConfirmation(
            "elevate-saas-credits-100k-monthly",
            "100k credits / month",
            "This recurring add-on will be added to the subscription and charged automatically each subscription period.",
            "Additional recurring charge",
            "$39 per month"), result.Confirmation);
        Assert.Null(zenmeter.AddedAddonSubscriptionId);
        Assert.Null(zenmeter.AddedAddonSkus);

        // act
        result = await service.AddTopUp(
            purchase.SessionId!,
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None,
            automaticPaymentConfirmed: true);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal("sub-1", zenmeter.AddedAddonSubscriptionId);
        Assert.Equal(["elevate-saas-credits-100k-monthly"], zenmeter.AddedAddonSkus);
    }

    [Fact]
    public async Task AddTopUp_WithFastSpringRecurringAddon_UpdatesSubscriptionAndWaitsForTheWebhook()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-fastspring-recurring-topup",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "sub-1",
            SubscriptionRefId = "provider-subscription-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var fastSpringUpdater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        fastSpringUpdater
            .Setup(updater => updater.EstimateRecurringAddon(
                "provider-subscription-1",
                "elevate-saas-credits-100k-monthly",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringSubscriptionProrationEstimate("$10.00", "$100.00", "2026-08-28"));
        fastSpringUpdater
            .Setup(updater => updater.AddRecurringAddon(
                "provider-subscription-1",
                "elevate-saas-credits-100k-monthly",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            fastSpringSubscriptionUpdater: fastSpringUpdater.Object);

        // act
        var result = await service.AddTopUp(
            "session-fastspring-recurring-topup",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal(new ZenmeterTopUpConfirmation(
            "elevate-saas-credits-100k-monthly",
            "100k credits / month",
            "This recurring add-on will be added to the existing subscription and billed automatically each subscription period. The charge for the current period is prorated and will use the saved billing details.",
            "Prorated charge today",
            "$10.00",
            "Recurring add-on charge from 2026-08-28",
            "$100.00"), result.Confirmation);
        fastSpringUpdater.Verify(
            updater => updater.AddRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fastSpringUpdater.Verify(
            updater => updater.EstimateRecurringAddon(
                "provider-subscription-1",
                "elevate-saas-credits-100k-monthly",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // act
        result = await service.AddTopUp(
            "session-fastspring-recurring-topup",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None,
            automaticPaymentConfirmed: true);

        // assert
        Assert.True(result.Succeeded);
        Assert.Null(result.RedirectUrl);
        Assert.False(string.IsNullOrWhiteSpace(result.OperationId));
        var subscriptionReadsBeforeRepeatedTopUp = zenmeter.GetSubscriptionCalls;
        var repeated = await service.AddTopUp(
            "session-fastspring-recurring-topup",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);
        Assert.Equal(result, repeated);
        Assert.Equal(subscriptionReadsBeforeRepeatedTopUp, zenmeter.GetSubscriptionCalls);
        fastSpringUpdater.Verify(
            updater => updater.AddRecurringAddon(
                "provider-subscription-1",
                "elevate-saas-credits-100k-monthly",
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Null(zenmeter.AddedAddonSubscriptionId);
        Assert.Null(zenmeter.AddedAddonSkus);
    }

    [Fact]
    public async Task AddTopUp_ForFastSpringRecurringAddon_ReturnsProratedConfirmationWithoutUpdatingSubscription()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-fastspring-recurring-topup-estimate",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "sub-1",
            SubscriptionRefId = "provider-subscription-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var fastSpringUpdater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        fastSpringUpdater
            .Setup(updater => updater.EstimateRecurringAddon(
                "provider-subscription-1",
                "elevate-saas-credits-100k-monthly",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringSubscriptionProrationEstimate("$12.34", "$149.00", "2026-08-28"));
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            fastSpringSubscriptionUpdater: fastSpringUpdater.Object);

        // act
        var result = await service.AddTopUp(
            "session-fastspring-recurring-topup-estimate",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal(new ZenmeterTopUpConfirmation(
            "elevate-saas-credits-100k-monthly",
            "100k credits / month",
            "This recurring add-on will be added to the existing subscription and billed automatically each subscription period. The charge for the current period is prorated and will use the saved billing details.",
            "Prorated charge today",
            "$12.34",
            "Recurring add-on charge from 2026-08-28",
            "$149.00"), result.Confirmation);
        fastSpringUpdater.Verify(
            updater => updater.EstimateRecurringAddon(
                "provider-subscription-1",
                "elevate-saas-credits-100k-monthly",
                It.IsAny<CancellationToken>()),
            Times.Once);
        fastSpringUpdater.Verify(
            updater => updater.AddRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Null(zenmeter.AddedAddonSkus);
    }

    [Fact]
    public async Task AddTopUp_WhenFastSpringRecurringEstimateFails_ReturnsFastSpringResponse()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-fastspring-recurring-topup-estimate-failed",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "sub-1",
            SubscriptionRefId = "provider-subscription-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var fastSpringUpdater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        fastSpringUpdater
            .Setup(updater => updater.EstimateRecurringAddon(
                "provider-subscription-1",
                "elevate-saas-credits-100k-monthly",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FastSpringApiRequestException(
                HttpStatusCode.BadRequest,
                """{"message":"Proration is not allowed for this subscription."}"""));
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            fastSpringSubscriptionUpdater: fastSpringUpdater.Object);

        // act
        var result = await service.AddTopUp(
            "session-fastspring-recurring-topup-estimate-failed",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);

        // assert
        Assert.False(result.Succeeded);
        Assert.Equal("fastspring_request_failed", result.Code);
        Assert.Equal(
            "FastSpring rejected the request: Proration is not allowed for this subscription.",
            result.Message);
        fastSpringUpdater.Verify(
            updater => updater.AddRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Null(zenmeter.AddedAddonSkus);
    }

    [Fact]
    public async Task AddTopUp_WhenFastSpringRecurringUpdateFails_DoesNotProvisionZenmeterAddon()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-fastspring-recurring-topup-failed",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "sub-1",
            SubscriptionRefId = "provider-subscription-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var fastSpringUpdater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        fastSpringUpdater
            .Setup(updater => updater.AddRecurringAddon(
                "provider-subscription-1",
                "elevate-saas-credits-100k-monthly",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FastSpringApiRequestException(
                HttpStatusCode.OK,
                """
                {
                  "subscriptions": [
                    {
                      "subscription": "provider-subscription-1",
                      "action": "subscription.update",
                      "result": "error",
                      "error": {
                        "subscription": "Subscription update is not allowed."
                      }
                    }
                  ]
                }
                """));
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            fastSpringSubscriptionUpdater: fastSpringUpdater.Object);

        // act
        var result = await service.AddTopUp(
            "session-fastspring-recurring-topup-failed",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None,
            automaticPaymentConfirmed: true);

        // assert
        Assert.False(result.Succeeded);
        Assert.Equal("fastspring_request_failed", result.Code);
        Assert.Equal(
            "FastSpring rejected the request: Subscription update is not allowed.",
            result.Message);
        fastSpringUpdater.VerifyAll();
        Assert.Null(zenmeter.AddedAddonSkus);
        Assert.Null(store.Get("session-fastspring-recurring-topup-failed")!.PendingTopUp);
    }

    [Fact]
    public async Task AddTopUp_WhenFastSpringRecurringSubscriptionReferenceIsMissing_ReturnsSpecificUnavailableFailure()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-fastspring-recurring-topup-missing-reference",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "sub-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var fastSpringUpdater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            fastSpringSubscriptionUpdater: fastSpringUpdater.Object);

        // act
        var result = await service.AddTopUp(
            "session-fastspring-recurring-topup-missing-reference",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);

        // assert
        Assert.False(result.Succeeded);
        Assert.Equal("top_up_unavailable", result.Code);
        Assert.Contains("no FastSpring subscription reference", result.Message);
        fastSpringUpdater.Verify(
            updater => updater.AddRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Null(zenmeter.AddedAddonSkus);
    }

    [Fact]
    public async Task AddTopUp_WhenFastSpringRecurringPendingTopUpTimedOut_ResumesExistingOperation()
    {
        // arrange
        var pending = new ZenmeterPendingTopUp(
            "operation-recurring-expired",
            "elevate-saas-credits-100k-monthly",
            "order-recurring-expired",
            0,
            ZenmeterRenewalBehavior.RenewsWithSubscription,
            ZenmeterCheckoutStatuses.Pending)
        {
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-fastspring-recurring-topup-expired",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "sub-1",
            SubscriptionRefId = "provider-subscription-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed,
            PendingTopUp = pending
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var fastSpringUpdater = new Mock<IFastSpringSubscriptionUpdater>(MockBehavior.Strict);
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            fastSpringSubscriptionUpdater: fastSpringUpdater.Object);

        // act
        var result = await service.AddTopUp(
            "session-fastspring-recurring-topup-expired",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);

        // assert
        Assert.Equal(BillingTopUpResults.Success(operationId: pending.OperationId), result);
        Assert.Equal(0, zenmeter.GetSubscriptionCalls);
        fastSpringUpdater.Verify(
            updater => updater.AddRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddTopUp_WithRecurringAddonOnStripe_RejectsTheTopUp()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-stripe-recurring-topup",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "sub-1",
            BillingSystem = BillingSystem.Stripe,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out _, store: store);

        // act
        var result = await service.AddTopUp(
            "session-stripe-recurring-topup",
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);

        // assert
        Assert.False(result.Succeeded);
        Assert.Equal("top_up_unavailable", result.Code);
        Assert.Contains("Stripe billing", result.Message);
        Assert.Equal(0, zenmeter.GetSubscriptionCalls);
        Assert.Null(zenmeter.AddedAddonSkus);
    }

    [Fact]
    public async Task AddTopUp_WithRecurringAddonAlreadyOnSubscription_AddsAnotherInstance()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = SubscriptionWithAddonMeterGrant("sub-1")
        };
        var service = CreateService(zenmeter, out _);

        // act
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-duplicate-recurring-topup",
            CancellationToken.None);
        var result = await service.AddTopUp(
            purchase.SessionId!,
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Confirmation);
        Assert.Null(zenmeter.AddedAddonSkus);

        // act
        result = await service.AddTopUp(
            purchase.SessionId!,
            "elevate-saas-credits-100k-monthly",
            CancellationToken.None,
            automaticPaymentConfirmed: true);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal("sub-1", zenmeter.AddedAddonSubscriptionId);
        Assert.Equal(["elevate-saas-credits-100k-monthly"], zenmeter.AddedAddonSkus);
    }

    [Fact]
    public async Task GetCheckoutInfo_WithInvalidAddonSku_RejectsTheRequest()
    {
        // arrange
        var service = CreateService(new StubZenmeterManagementClient(), out _);

        // act
        var checkout = await service.GetCheckoutInfo(
            "elevate-saas-scale-monthly",
            "missing-addon-sku",
            CancellationToken.None);

        // assert
        Assert.NotNull(checkout);
        Assert.False(checkout.CanPurchase);
        Assert.Contains("missing-addon-sku", checkout.UnavailableReason);
    }

    [Fact]
    public async Task GetCheckoutInfo_WithUnknownPlanSku_DoesNotRequestCompatibleAddons()
    {
        // arrange
        var service = CreateService(new StubZenmeterManagementClient(), out _, out var catalog);

        // act
        var checkout = await service.GetCheckoutInfo(
            BillingSystem.None,
            "missing-plan-sku",
            null,
            CancellationToken.None);

        // assert
        Assert.Null(checkout);
        Assert.Empty(catalog.RequestedAddonBillingSystems);
    }

    [Fact]
    public async Task AddTopUp_WithPaidTopUp_StartsCheckoutAndWaitsForProviderVerificationBeforeProvisioning()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-paid-topup",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "sub-1",
            SubscriptionRefId = "provider-sub-1",
            BillingSystem = BillingSystem.Stripe,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var subscription = Subscription("sub-1");
        var zenmeter = new StubZenmeterManagementClient { Subscription = subscription };
        var service = CreateService(zenmeter, out _, store: store);

        // act
        var result = await service.AddTopUp(
            "session-paid-topup",
            "elevate-saas-credits-50k-onetime",
            CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal("https://checkout.stripe.test/session", result.RedirectUrl);
        Assert.NotNull(result.OperationId);
        Assert.Null(zenmeter.AddedAddonSkus);
        var subscriptionReadsBeforeRepeatedTopUp = zenmeter.GetSubscriptionCalls;
        var repeated = await service.AddTopUp(
            "session-paid-topup",
            "elevate-saas-credits-50k-onetime",
            CancellationToken.None);
        Assert.Equal(result.OperationId, repeated.OperationId);
        Assert.Equal(result.RedirectUrl, repeated.RedirectUrl);
        Assert.Equal(subscriptionReadsBeforeRepeatedTopUp, zenmeter.GetSubscriptionCalls);
        var pending = await service.GetTopUpStatus(
            "session-paid-topup",
            result.OperationId!,
            null,
            CancellationToken.None);
        Assert.Equal(ZenmeterCheckoutStatuses.Pending, pending.Status);

        subscription.Addons.Add(new Zm.SubscriptionAddonModel
        {
            Id = "topup-addon-1",
            Sku = "elevate-saas-credits-50k-onetime",
            OfferingName = "50k credits"
        });
        var completed = await service.GetTopUpStatus(
            "session-paid-topup",
            result.OperationId!,
            null,
            CancellationToken.None);

        Assert.Equal(ZenmeterCheckoutStatuses.Pending, completed.Status);
        var workspace = await service.GetWorkspace("session-paid-topup", CancellationToken.None);
        Assert.DoesNotContain(workspace!.Events, entry => entry.Contains("Provisioned paid top-up"));
    }

    [Fact]
    public async Task GetTopUpStatus_WithCompletedFastSpringOrder_VerifiesTheOrderBeforeAddingTheAddon()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-fastspring-topup",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "zm-sub-1",
            SubscriptionRefId = "provider-sub-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient { Subscription = Subscription("zm-sub-1") };
        var verifier = new StubFastSpringBillingPaymentVerifier();
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            fastSpringPaymentVerifier: verifier);

        // act
        var started = await service.AddTopUp(
            "session-fastspring-topup",
            "elevate-saas-credits-50k-onetime",
            CancellationToken.None);
        var completed = await service.GetTopUpStatus(
            "session-fastspring-topup",
            started.OperationId!,
            "fastspring-order-1",
            CancellationToken.None);

        // assert
        Assert.True(started.Succeeded);
        Assert.Equal("https://checkout.fastspring.test/session", started.RedirectUrl);
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, completed.Status);
        Assert.Equal("zm-sub-1", zenmeter.AddedAddonSubscriptionId);
        Assert.Equal(["elevate-saas-credits-50k-onetime"], zenmeter.AddedAddonSkus);
        Assert.Equal("fastspring-order-1", verifier.Payment!.ProviderOrderRefId);
        Assert.Equal(started.OperationId, verifier.Payment.OperationId);
        Assert.Equal("zm-sub-1", verifier.Payment.TargetSubscriptionId);
    }

    [Fact]
    public async Task GetTopUpStatus_WhenVerificationFails_AllowsStartingANewCheckout()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-failed-topup",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "zm-sub-1",
            SubscriptionRefId = "provider-sub-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed
        });
        var zenmeter = new StubZenmeterManagementClient { Subscription = Subscription("zm-sub-1") };
        var verifier = new StubFastSpringBillingPaymentVerifier(BillingPaymentVerification.Failed("Payment failed."));
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            fastSpringPaymentVerifier: verifier);

        // act
        var started = await service.AddTopUp(
            "session-failed-topup",
            "elevate-saas-credits-50k-onetime",
            CancellationToken.None);
        var failed = await service.GetTopUpStatus(
            "session-failed-topup",
            started.OperationId!,
            "fastspring-order-failed",
            CancellationToken.None);
        var failedAgain = await service.GetTopUpStatus(
            "session-failed-topup",
            started.OperationId!,
            "fastspring-order-failed",
            CancellationToken.None);
        var retried = await service.AddTopUp(
            "session-failed-topup",
            "elevate-saas-credits-50k-onetime",
            CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Failed, failed.Status);
        Assert.Equal("Payment failed.", failed.Error);
        Assert.Equal(ZenmeterCheckoutStatuses.Failed, failedAgain.Status);
        Assert.Equal("Payment failed.", failedAgain.Error);
        Assert.True(retried.Succeeded);
        Assert.NotEqual(started.OperationId, retried.OperationId);
    }

    [Fact]
    public async Task AddTopUp_WhenPreviousTopUpTimedOut_AllowsStartingANewCheckout()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "session-timed-out-topup",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            SubscriptionId = "zm-sub-1",
            SubscriptionRefId = "provider-sub-1",
            BillingSystem = BillingSystem.FastSpring,
            User = ZenmeterDemoTestExtensions.UserDetails,
            CheckoutStatus = ZenmeterCheckoutStatuses.Completed,
            PendingTopUp = new ZenmeterPendingTopUp(
                "expired-operation",
                "elevate-saas-credits-50k-onetime",
                "expired-order",
                0,
                ZenmeterRenewalBehavior.OneTime,
                ZenmeterCheckoutStatuses.Pending,
                "https://checkout.fastspring.test/expired")
            {
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
            }
        });
        var zenmeter = new StubZenmeterManagementClient { Subscription = Subscription("zm-sub-1") };
        var service = CreateService(
            zenmeter,
            out _,
            store: store,
            billingOptions: new BillingOptions
            {
                ProvisioningPoll = new ProvisioningPollOptions { TimeoutSeconds = 30 }
            });

        // act
        var retried = await service.AddTopUp(
            "session-timed-out-topup",
            "elevate-saas-credits-50k-onetime",
            CancellationToken.None);

        // assert
        Assert.True(retried.Succeeded);
        Assert.NotEqual("expired-operation", retried.OperationId);
        Assert.NotEqual("https://checkout.fastspring.test/expired", retried.RedirectUrl);
    }

    [Fact]
    public async Task Purchase_AfterFailedApiCall_CanRetryWithTheSameCheckoutRequestId()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient();
        var service = CreateService(zenmeter, out var customers);

        var failed = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-retry",
            CancellationToken.None);

        // act
        zenmeter.Subscription = Subscription("sub-1");
        var retried = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-retry",
            CancellationToken.None);

        // assert
        Assert.Null(failed.SessionId);
        Assert.NotNull(retried.SessionId);
        Assert.Equal(2, customers.CreateCalls);
    }

    [Fact]
    public async Task GetBillingStatus_WithProviderRefs_LooksUpTheSubscriptionByProviderReference()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(
            zenmeter,
            out _,
            billingOptions: new BillingOptions { DefaultBillingSystem = BillingSystem.Stripe });
        var purchase = await service.Purchase(
            BillingSystem.Stripe,
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            ZenmeterDemoTestExtensions.UserInput,
            "zm-checkout-provider-ref",
            CancellationToken.None);

        // act
        var status = await service.GetBillingStatus(
            purchase.SessionId!,
            "fastspring-order-1",
            "fastspring-subscription-1",
            CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, status.Status);
        Assert.Equal("sub-1", status.SubscriptionId);
        Assert.Null(zenmeter.LookupOrderRefId);
        Assert.Equal("fastspring-subscription-1", zenmeter.LookupSubscriptionRefId);
    }

    [Fact]
    public async Task GetBillingStatus_WithFastSpringOrderRef_UsesTheProviderOrderReferenceForLookup()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(
            zenmeter,
            out _,
            billingOptions: new BillingOptions { DefaultBillingSystem = BillingSystem.Stripe });
        var purchase = await service.Purchase(
            BillingSystem.Stripe,
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            ZenmeterDemoTestExtensions.UserInput,
            "zm-checkout-provider-order-ref",
            CancellationToken.None);

        // act
        var status = await service.GetBillingStatus(
            purchase.SessionId!,
            "fastspring-order-1",
            null,
            CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Completed, status.Status);
        Assert.Equal("sub-1", status.SubscriptionId);
        Assert.Equal("fastspring-order-1", zenmeter.LookupOrderRefId);
        Assert.Null(zenmeter.LookupSubscriptionRefId);
    }

    [Theory]
    [InlineData(BillingSystem.None)]
    [InlineData(BillingSystem.Stripe)]
    [InlineData(BillingSystem.FastSpring)]
    public async Task GetCheckoutInfo_WithRequestedBillingSystem_RequestsPricingForThatSystem(BillingSystem billingSystem)
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient();
        var service = CreateService(zenmeter, out _, out var catalog);

        // act
        await service.GetCheckoutInfo(billingSystem, "elevate-saas-scale-monthly", null, CancellationToken.None);

        // assert
        Assert.Equal(billingSystem, Assert.Single(catalog.RequestedPricingBillingSystems));
        Assert.Equal(billingSystem, Assert.Single(catalog.RequestedAddonBillingSystems));
    }

    [Fact]
    public async Task GetWorkspace_WithMissingSessionUser_DoesNotCreateOne()
    {
        // arrange
        var store = new InMemoryZenmeterDemoSessionStore();
        store.Save(new ZenmeterDemoSession
        {
            SessionId = "zmsess-readonly",
            CustomerName = "Acme",
            TierKey = "scale",
            PlanSku = "elevate-saas-scale-monthly",
            Period = ZenmeterOfferingPeriod.Monthly,
            CustomerId = "customer-1",
            SubscriptionId = "sub-1",
            User = ZenmeterDemoTestExtensions.UserDetails
        });
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out _, store: store);

        // act
        var workspace = await service.GetWorkspace("zmsess-readonly", CancellationToken.None);

        // assert
        Assert.Equal(0, zenmeter.CreateUserCalls);
        Assert.Contains(
            "Subscription user demo-user is missing from the Zenmeter subscription.",
            workspace!.DataIssues);
    }

    [Fact]
    public async Task GetWorkspace_WhenCalled_DoesNotRequestBasePlanPrices()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1")
        };
        var service = CreateService(zenmeter, out _, out var catalog);
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-workspace-prices",
            CancellationToken.None);
        catalog.RequestedPricingBillingSystems.Clear();
        catalog.RequestedAddonBillingSystems.Clear();

        // act
        await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Equal(1, catalog.PricingShellCalls);
        Assert.Empty(catalog.RequestedPricingBillingSystems);
        Assert.Equal(BillingSystem.None, Assert.Single(catalog.RequestedAddonBillingSystems));
    }

    [Fact]
    public async Task EnsureUser_WhenCreateUserConflicts_ReloadsTheExistingUser()
    {
        // arrange
        var user = new Zm.SubscriptionUserModel
        {
            SubscriptionUserId = "zmsu-demo-user",
            ExternalUserId = "demo-user",
            FirstName = "Demo",
            LastName = "User",
            Email = "demo-user@elevate.example",
            Status = Zm.SubscriptionUserStatus.Enabled
        };
        var zenmeter = new StubZenmeterManagementClient
        {
            CreateUserException = new ManagementApiException("Conflict", HttpStatusCode.Conflict, "")
        };
        zenmeter.QueuedUserLists.Enqueue([]);
        zenmeter.QueuedUserLists.Enqueue([user]);
        var provisioner = new ZenmeterSubscriptionUserProvisioner(zenmeter);

        // act
        var result = await provisioner.EnsureUser(
            "sub-1",
            ZenmeterDemoTestExtensions.UserDetails,
            CancellationToken.None);

        // assert
        Assert.Equal("demo-user", result.ExternalUserId);
        Assert.Equal(1, zenmeter.CreateUserCalls);
    }

    [Fact]
    public async Task AddTopUp_WithConcurrentMutations_AppendsAllEvents()
    {
        // arrange
        var zenmeter = new StubZenmeterManagementClient
        {
            Subscription = Subscription("sub-1"),
            AddAddonDelay = TimeSpan.FromMilliseconds(1)
        };
        var service = CreateService(zenmeter, out _);
        var purchase = await service.Purchase(
            "elevate-saas-scale-monthly",
            null,
            "Acme",
            "zm-checkout-concurrent-topup",
            CancellationToken.None);

        // act
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => service.AddTopUp(
                purchase.SessionId!,
                "elevate-saas-credits-50k-onetime",
                CancellationToken.None));
        var results = await Task.WhenAll(tasks);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(
            20,
            workspace!.Events.Count(entry => entry.Contains("Added top-up elevate-saas-credits-50k-onetime")));
    }

    private static IZenmeterDemo CreateService(
        StubZenmeterManagementClient zenmeter,
        out StubCustomersClient customers,
        string webUrl = "",
        InMemoryZenmeterDemoSessionStore? store = null,
        BillingOptions? billingOptions = null,
        IFastSpringBillingPaymentVerifier? fastSpringPaymentVerifier = null,
        IStripeBillingPaymentVerifier? stripePaymentVerifier = null,
        IFastSpringSubscriptionUpdater? fastSpringSubscriptionUpdater = null,
        StubZenmeterConsumptionClient? consumptionClient = null) =>
        CreateService(
            zenmeter,
            out customers,
            out _,
            webUrl,
            store,
            billingOptions,
            fastSpringPaymentVerifier,
            stripePaymentVerifier,
            fastSpringSubscriptionUpdater,
            consumptionClient);

    private static IZenmeterDemo CreateService(
        StubZenmeterManagementClient zenmeter,
        out StubCustomersClient customers,
        out StubZenmeterPricingCatalog catalog,
        string webUrl = "",
        InMemoryZenmeterDemoSessionStore? store = null,
        BillingOptions? billingOptions = null,
        IFastSpringBillingPaymentVerifier? fastSpringPaymentVerifier = null,
        IStripeBillingPaymentVerifier? stripePaymentVerifier = null,
        IFastSpringSubscriptionUpdater? fastSpringSubscriptionUpdater = null,
        StubZenmeterConsumptionClient? consumptionClient = null)
    {
        customers = new StubCustomersClient();
        catalog = new StubZenmeterPricingCatalog();
        store ??= new InMemoryZenmeterDemoSessionStore();
        var guard = new MemoryCacheCheckoutRequestGuard(new MemoryCache(new MemoryCacheOptions()));
        var provisioner = new ZenmeterSubscriptionUserProvisioner(zenmeter);
        var checkoutService = new BillingCheckoutService(
            [
                new NoneBillingCheckoutProvider(zenmeter),
                new StubExternalBillingCheckoutProvider(BillingSystem.Stripe),
                new StubExternalBillingCheckoutProvider(BillingSystem.FastSpring)
            ],
            Options.Create(billingOptions ?? new BillingOptions()));
        var billingStatus = new ZenmeterBillingStatusService(
            zenmeter,
            store,
            provisioner,
            Options.Create(billingOptions ?? new BillingOptions()),
            NullLogger<ZenmeterBillingStatusService>.Instance);
        var purchase = new ZenmeterPurchaseService(
            catalog,
            customers,
            store,
            guard,
            provisioner,
            billingStatus,
            checkoutService,
            NullLogger<ZenmeterPurchaseService>.Instance);
        var usage = new ZenmeterUsageService(
            consumptionClient ?? new StubZenmeterConsumptionClient(),
            store,
            NullLogger<ZenmeterUsageService>.Instance);
        var topUpStarter = new BillingCheckoutTopUpStarter(checkoutService);
        var topUpProviders = new IBillingTopUpPurchaseProvider[]
        {
            new NoneBillingTopUpPurchaseProvider(zenmeter),
            new StripeBillingTopUpPurchaseProvider(
                topUpStarter,
                zenmeter,
                stripePaymentVerifier ?? new StubStripeBillingPaymentVerifier()),
            new FastSpringBillingTopUpPurchaseProvider(
                fastSpringSubscriptionUpdater ?? CreateDefaultFastSpringSubscriptionUpdater(),
                topUpStarter,
                zenmeter,
                fastSpringPaymentVerifier ?? new StubFastSpringBillingPaymentVerifier())
        };
        var topUpPurchaseProvider = new TopUpPurchaseProvider(topUpProviders);
        var topUpPolicy = new ZenmeterTopUpPolicy(topUpPurchaseProvider);
        var workspace = new ZenmeterWorkspaceQuery(
            catalog,
            zenmeter,
            store,
            topUpPolicy,
            Options.Create(new NalpeironOptions { WebUrl = webUrl }));
        var topUps = new ZenmeterTopUpService(
            catalog,
            zenmeter,
            store,
            topUpPolicy,
            topUpPurchaseProvider,
            Options.Create(billingOptions ?? new BillingOptions()),
            NullLogger<ZenmeterTopUpService>.Instance);

        return new ZenmeterDemoFacade(
            catalog,
            purchase,
            workspace,
            usage,
            topUps,
            store);
    }

    private static IFastSpringSubscriptionUpdater CreateDefaultFastSpringSubscriptionUpdater()
    {
        var updater = new Mock<IFastSpringSubscriptionUpdater>();
        updater
            .Setup(client => client.AddRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        updater
            .Setup(client => client.EstimateRecurringAddon(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringSubscriptionProrationEstimate("$10.00", "$100.00", "2026-08-28"));
        return updater.Object;
    }

    private static Zm.SubscriptionModel Subscription(string id) =>
        new()
        {
            Id = id,
            Customer = new Zm.SubscriptionCustomerModel
            {
                Id = "customer-1",
                Name = "Acme",
                AccountRefId = "_demo-z2-customer"
            },
            Offering = new Zm.SubscriptionOfferingModel
            {
                ProductName = "Elevate SaaS",
                ProductId = "product-1",
                OfferingName = "Scale",
                Sku = "elevate-saas-scale-monthly"
            },
            BillingReference = new Zm.BillingReferenceModel
            {
                OrderRefId = "_demo-z2-order"
            },
            StatusInfo = new Zm.SubscriptionStatusModel
            {
                Status = Zm.SubscriptionStatus.Active,
                Trial = false
            },
            CreatedAt = DateTimeOffset.Parse("2026-06-16T00:00:00Z"),
            BusinessModel = "subscription-pool",
            BusinessModelId = "business-model-1",
            UserCount = 0,
            Addons = [],
            BillingPeriod = Zm.BillingPeriod.Monthly,
            CurrentUsagePeriodStart = DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            NextUsageResetAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z")
        };

    private static IReadOnlyList<Zm.SubscriptionFeatureListItemModel> DefaultFeatures() =>
    [
        UsageFeature("ai-campaign-draft", "AI campaign draft", "draft", "drafts", "credits")
    ];

    private static IReadOnlyList<Zm.SubscriptionMeterListItemModel> DefaultMeters() =>
    [
        Meter("credits", "Credits", "credit", "credits",
        [
            MeterSource(Zm.GrantSourceKind.BaseOffering, null, 100000)
        ])
    ];

    private static ConsumptionResult Consumed(
        string requestedFeatureKey,
        long requestedAmount,
        decimal conversionRate,
        BalanceSnapshot balanceSnapshot) =>
        new()
        {
            Consumed = true,
            Consumption = Snapshot(
                requestedFeatureKey,
                requestedAmount,
                conversionRate,
                balanceSnapshot)
        };

    private static ConsumedSubscriptionFeature Snapshot(
        string requestedFeatureKey,
        long requestedAmount,
        decimal conversionRate,
        BalanceSnapshot balanceSnapshot) =>
        new()
        {
            RequestedFeatureKey = requestedFeatureKey,
            RequestedAmount = requestedAmount,
            ConversionRate = conversionRate,
            BalanceSnapshot = balanceSnapshot
        };

    private static BalanceSnapshot MeterBalanceSnapshot(
        string key,
        IReadOnlyList<BalanceBucket> buckets) =>
        new()
        {
            BalanceOwner = new BalanceOwnerReference
            {
                Key = key,
                Kind = BalanceOwnerKind.Meter
            },
            UsageBuckets = buckets.ToList()
        };

    private static BalanceBucket Bucket(
        BucketType bucketType,
        decimal used,
        decimal available,
        long limit,
        string? subscriptionAddonId = null) =>
        new()
        {
            BucketType = bucketType,
            Used = used,
            Available = available,
            Limit = limit,
            SubscriptionAddonId = subscriptionAddonId
        };

    private static IReadOnlyList<Zm.SubscriptionMeterListItemModel> AddonMeterGrantMeters(
        long baseGrant = 25000,
        long addonGrant = 50000) =>
    [
        Meter("credits", "Credits", "credit", "credits",
        [
            MeterSource(Zm.GrantSourceKind.BaseOffering, null, baseGrant),
            MeterSource(Zm.GrantSourceKind.Addon, "zm-sub-addon-1", addonGrant)
        ])
    ];

    private static IReadOnlyList<Zm.SubscriptionMeterListItemModel> TwoAddonMeterGrantMeters() =>
    [
        Meter("credits", "Credits", "credit", "credits",
        [
            MeterSource(Zm.GrantSourceKind.BaseOffering, null, 500),
            MeterSource(Zm.GrantSourceKind.Addon, "zm-sub-addon-recurring", 500),
            MeterSource(Zm.GrantSourceKind.Addon, "zm-sub-addon-topup", 500)
        ])
    ];

    private static IReadOnlyList<Zm.SubscriptionFeatureListItemModel> AccessFeatures() =>
    [
        UsageFeature("ai-campaign-draft", "AI campaign draft", "draft", "drafts", "credits"),
        AccessFeature("team-workspace", "Team workspace",
            [Source(Zm.GrantSourceKind.BaseOffering, null, Zm.Access.Enabled)]),
        AccessFeature("sso", "SSO",
            [Source(Zm.GrantSourceKind.BaseOffering, null, Zm.Access.Disabled)])
    ];

    private static IReadOnlyList<Zm.SubscriptionFeatureListItemModel> AddonAccessFeatures() =>
    [
        UsageFeature("ai-campaign-draft", "AI campaign draft", "draft", "drafts", "credits"),
        AccessFeature("team-workspace", "Team workspace",
            [Source(Zm.GrantSourceKind.BaseOffering, null, Zm.Access.Enabled)]),
        AccessFeature(
            "audit-logs",
            "Audit logs",
            [
                Source(Zm.GrantSourceKind.BaseOffering, null, Zm.Access.Disabled),
                Source(Zm.GrantSourceKind.Addon, "zm-sub-addon-security", Zm.Access.Enabled)
            ]),
        AccessFeature(
            "sso",
            "SSO",
            [
                Source(Zm.GrantSourceKind.BaseOffering, null, Zm.Access.Disabled),
                Source(Zm.GrantSourceKind.Addon, "zm-sub-addon-security", Zm.Access.Enabled)
            ])
    ];

    private static Zm.SubscriptionFeatureListItemModel UsageFeature(
        string key,
        string displayName,
        string unitName,
        string unitPluralName,
        string meterKey) =>
        new()
        {
            Reference = new Zm.FeatureReferenceModel
            {
                Key = key,
                DisplayName = displayName
            },
            Unit = new Zm.UnitModel
            {
                Name = unitName,
                PluralName = unitPluralName
            },
            FeatureKind = Zm.FeatureKind.Quantitative,
            MeterKey = meterKey,
            Sources = [Source(Zm.GrantSourceKind.BaseOffering, null, Zm.Access.Enabled)]
        };

    private static Zm.SubscriptionFeatureListItemModel AccessFeature(
        string key,
        string displayName,
        IReadOnlyList<Zm.FeatureGrantSourceModel> sources) =>
        new()
        {
            Reference = new Zm.FeatureReferenceModel
            {
                Key = key,
                DisplayName = displayName
            },
            FeatureKind = Zm.FeatureKind.Access,
            Sources = sources.ToList()
        };

    private static Zm.FeatureGrantSourceModel Source(
        Zm.GrantSourceKind sourceKind,
        string? subscriptionAddonId,
        Zm.Access access) =>
        new()
        {
            SourceKind = sourceKind,
            SubscriptionAddonId = subscriptionAddonId,
            Access = access
        };

    private static Zm.MeterGrantSourceModel MeterSource(
        Zm.GrantSourceKind sourceKind,
        string? subscriptionAddonId,
        long includedAmount) =>
        new()
        {
            SourceKind = sourceKind,
            SubscriptionAddonId = subscriptionAddonId,
            UsageGrants = new Zm.ScopedUsageGrantsModel
            {
                Shared = new Zm.UsageGrantModel { IncludedAmount = includedAmount }
            }
        };

    private static Zm.SubscriptionMeterListItemModel Meter(
        string key,
        string displayName,
        string unitName,
        string unitPluralName,
        IReadOnlyList<Zm.MeterGrantSourceModel> sources) =>
        new()
        {
            Reference = new Zm.MeterReferenceModel
            {
                Key = key,
                DisplayName = displayName
            },
            Unit = new Zm.UnitModel
            {
                Name = unitName,
                PluralName = unitPluralName
            },
            Sources = sources.ToList()
        };

    private static Zm.SubscriptionModel SubscriptionWithAddonMeterGrant(
        string id,
        long baseGrant = 25000,
        long addonGrant = 50000)
    {
        var subscription = Subscription(id);
        subscription.Addons =
        [
            Addon(
                "zm-sub-addon-1",
                "elevate-saas-credits-100k-monthly",
                "100k credits / month",
                Zm.AddonRenewalBehavior.RenewsWithSubscription,
                Zm.BillingPeriod.Monthly)
        ];
        return subscription;
    }

    private static Zm.SubscriptionModel SubscriptionWithTwoAddonMeterGrants(string id)
    {
        var subscription = Subscription(id);
        subscription.Addons =
        [
            Addon(
                "zm-sub-addon-recurring",
                "elevate-saas-credits-500-monthly",
                "500 credits",
                Zm.AddonRenewalBehavior.RenewsWithSubscription,
                Zm.BillingPeriod.Monthly),
            Addon(
                "zm-sub-addon-topup",
                "elevate-saas-credits-500-onetime",
                "500 credits",
                Zm.AddonRenewalBehavior.OneTime,
                duration: new Zm.Interval { Type = Zm.IntervalType.Month, Count = 1 },
                expiresAt: DateTimeOffset.Parse("2026-07-16T00:00:00Z"))
        ];
        return subscription;
    }

    private static Zm.SubscriptionModel SubscriptionWithAddonAccessFeatures(string id)
    {
        var subscription = Subscription(id);
        subscription.Addons =
        [
            Addon(
                "zm-sub-addon-security",
                "elevate-saas-security-suite-1m",
                "Security Suite",
                Zm.AddonRenewalBehavior.OneTime,
                duration: new Zm.Interval { Type = Zm.IntervalType.Month, Count = 1 },
                expiresAt: DateTimeOffset.Parse("2026-07-16T00:00:00Z"))
        ];
        return subscription;
    }

    private static Zm.SubscriptionAddonModel Addon(
        string id,
        string sku,
        string name,
        Zm.AddonRenewalBehavior renewalBehavior,
        Zm.BillingPeriod? billingPeriod = null,
        Zm.Interval? duration = null,
        DateTimeOffset? expiresAt = null) =>
        new()
        {
            Id = id,
            Sku = sku,
            OfferingName = name,
            CreatedAt = DateTimeOffset.Parse("2026-06-16T00:00:00Z"),
            ExpiryDate = expiresAt,
            Term = new Zm.AddonOfferingTermModel
            {
                RenewalBehavior = renewalBehavior,
                BillingPeriod = billingPeriod,
                Duration = duration
            },
            StatusInfo = new Zm.SubscriptionAddonStatusModel
            {
                Status = Zm.AddonStatus.Active
            }
        };

    private sealed class StubZenmeterPricingCatalog : IZenmeterPricingCatalog
    {
        public List<BillingSystem> RequestedPricingBillingSystems { get; } = [];
        public List<BillingSystem> RequestedAddonBillingSystems { get; } = [];
        public int PricingShellCalls { get; private set; }

        public Task<ZenmeterCatalogPricing> GetPricingShell(CancellationToken cancellationToken)
        {
            PricingShellCalls++;
            return Task.FromResult(Pricing());
        }

        public Task<ZenmeterCatalogPricing> GetPricing(
            BillingSystem billingSystem,
            CancellationToken cancellationToken)
        {
            RequestedPricingBillingSystems.Add(billingSystem);
            return Task.FromResult(Pricing());
        }

        public Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddonShell(
            string baseOfferingSku,
            CancellationToken cancellationToken) =>
            Task.FromResult(Pricing().Tiers[0].AddOns);

        public Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddons(
            string baseOfferingSku,
            BillingSystem billingSystem,
            CancellationToken cancellationToken)
        {
            RequestedAddonBillingSystems.Add(billingSystem);
            return Task.FromResult(Pricing().Tiers[0].AddOns);
        }

        public Task<IReadOnlyDictionary<string, BillingPrice>?> TryGetPriceBook(
            BillingSystem billingSystem,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, BillingPrice>?>(null);

        public Task<ZenmeterCatalogPricing> GetPricing(
            IReadOnlyDictionary<string, BillingPrice> prices,
            CancellationToken cancellationToken) =>
            Task.FromResult(Pricing());

        public Task<IReadOnlyList<ZenmeterAddonPricing>> GetCompatibleAddons(
            string baseOfferingSku,
            IReadOnlyDictionary<string, BillingPrice> prices,
            CancellationToken cancellationToken) =>
            Task.FromResult(Pricing().Tiers[0].AddOns);

        private static ZenmeterCatalogPricing Pricing() =>
            new(
                "Elevate SaaS",
                "credits",
                [
                    new ZenmeterTierPricing(
                        "scale",
                        "Scale",
                        "For growth teams.",
                        "Popular",
                        true,
                        [
                            new ZenmeterOfferingPricing(
                                ZenmeterOfferingPeriod.Monthly,
                                "elevate-saas-scale-monthly",
                                IsTrial: false,
                                IsVisible: true,
                                Price: 149,
                                BillingLabel: "per month")
                        ],
                        100000,
                        [
                            "AI campaign draft",
                            "Team workspace",
                            "SSO"
                        ],
                        [
                            new ZenmeterAddonPricing(
                                "elevate-saas-credits-50k-onetime",
                                "50k credits",
                                "One-time shared credit pack.",
                                [],
                                ZenmeterAddonType.MeterTopUp,
                                50000,
                                29,
                                "one time",
                                ZenmeterRenewalBehavior.OneTime,
                                ZenmeterOfferingPeriod.Any,
                                IsVisible: true,
                                SortOrder: 0),
                            new ZenmeterAddonPricing(
                                "elevate-saas-credits-100k-monthly",
                                "100k credits / month",
                                "Recurring monthly credit pack.",
                                [],
                                ZenmeterAddonType.MeterTopUp,
                                100000,
                                39,
                                "per month",
                                ZenmeterRenewalBehavior.RenewsWithSubscription,
                                ZenmeterOfferingPeriod.Monthly,
                                IsVisible: true,
                                SortOrder: 1),
                            new ZenmeterAddonPricing(
                                "elevate-saas-seats-10-onetime",
                                "10 extra seats",
                                "One-time seat pack.",
                                [],
                                ZenmeterAddonType.Unknown,
                                10,
                                19,
                                "one time",
                                ZenmeterRenewalBehavior.OneTime,
                                ZenmeterOfferingPeriod.Any,
                                IsVisible: true,
                                SortOrder: 2),
                            new ZenmeterAddonPricing(
                                "elevate-saas-credits-250k-hidden",
                                "250k hidden credits",
                                "Hidden credit pack.",
                                [],
                                ZenmeterAddonType.MeterTopUp,
                                250000,
                                99,
                                "one time",
                                ZenmeterRenewalBehavior.OneTime,
                                ZenmeterOfferingPeriod.Any,
                                IsVisible: false,
                                SortOrder: 3),
                            new ZenmeterAddonPricing(
                                "elevate-saas-security-suite-1m",
                                "Security Suite",
                                "One-month security features.",
                                ["Audit logs", "SSO"],
                                ZenmeterAddonType.FeatureBundle,
                                0,
                                29,
                                "one month",
                                ZenmeterRenewalBehavior.OneTime,
                                ZenmeterOfferingPeriod.Any,
                                IsVisible: true,
                                SortOrder: 4)
                        ])
                ],
                [],
                new Dictionary<string, ZenmeterFeatureRatePricing>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ai-campaign-draft"] = new(12, "credit", "credits"),
                    ["journey-simulation"] = new(8, "credit", "credits"),
                    ["lead-enrichment"] = new(1, "credit", "credits"),
                    ["send-time-optimization"] = new(0.1m, "credit", "credits")
                });
    }

    private sealed class StubCustomersClient : ICustomersClient
    {
        public int CreateCalls { get; private set; }

        public Task<CustomerRef> CreateCustomer(string name, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(new CustomerRef("customer-1", "account-ref-1"));
        }
    }

    private sealed class StubZenmeterManagementClient : IZenmeterManagementClient
    {
        public Zm.SubscriptionModel? Subscription { get; set; }
        public IReadOnlyList<Zm.SubscriptionFeatureListItemModel>? Features { get; init; }
        public IReadOnlyList<Zm.SubscriptionMeterListItemModel>? Meters { get; init; }
        public Exception? CreateUserException { get; init; }
        public TimeSpan AddAddonDelay { get; init; }
        public Queue<IReadOnlyList<Zm.SubscriptionUserModel>> QueuedUserLists { get; } = new();
        public string? CustomerId { get; private set; }
        public IReadOnlyList<string>? Skus { get; private set; }
        public string? OrderRefId { get; private set; }
        public string? LookupOrderRefId { get; private set; }
        public string? LookupSubscriptionRefId { get; private set; }
        public string? CreatedUserSubscriptionId { get; private set; }
        public string? CreatedExternalUserId { get; private set; }
        public int CreateUserCalls { get; private set; }
        public int ListUsersCalls { get; private set; }
        public int GetSubscriptionCalls { get; private set; }
        public int GetMetersCalls { get; private set; }
        public string? AddedAddonSubscriptionId { get; private set; }
        public IReadOnlyList<string>? AddedAddonSkus { get; private set; }
        private readonly List<Zm.SubscriptionUserModel> _users = [];

        public Task<Zm.CatalogBusinessModelConfigurationModel?> GetBusinessModel(
            string businessModelId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Zm.CatalogBusinessModelConfigurationModel?>(null);

        public Task<Zm.CatalogCompatibleAddonListModel?> GetCompatibleAddons(
            string baseOfferingSku,
            CancellationToken cancellationToken) =>
            Task.FromResult<Zm.CatalogCompatibleAddonListModel?>(null);

        public Task<Zm.SubscriptionModel?> CreateSubscription(
            string customerId,
            IReadOnlyList<string> skus,
            string orderRefId,
            CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            Skus = skus;
            OrderRefId = orderRefId;
            return Task.FromResult(Subscription);
        }

        public Task<Zm.SubscriptionModel?> GetSubscription(string subscriptionId,
            CancellationToken cancellationToken)
        {
            GetSubscriptionCalls++;
            return Task.FromResult(Subscription);
        }

        public Task<Zm.SubscriptionModel?> LookupSubscription(
            string? orderRefId,
            string? subscriptionRefId,
            CancellationToken cancellationToken)
        {
            LookupOrderRefId = orderRefId;
            LookupSubscriptionRefId = subscriptionRefId;
            return Task.FromResult(Subscription);
        }

        public Task<IReadOnlyList<Zm.SubscriptionFeatureListItemModel>> GetFeatures(
            string subscriptionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Features ?? DefaultFeatures());

        public Task<IReadOnlyList<Zm.SubscriptionMeterListItemModel>> GetMeters(
            string subscriptionId,
            CancellationToken cancellationToken)
        {
            GetMetersCalls++;
            return Task.FromResult(Meters ?? DefaultMeters());
        }

        public async Task AddAddons(
            string subscriptionId,
            IReadOnlyList<string> skus,
            string? orderRefId,
            BillingSystem? billingSystem,
            CancellationToken cancellationToken)
        {
            if (AddAddonDelay > TimeSpan.Zero)
            {
                await Task.Delay(AddAddonDelay, cancellationToken);
            }

            AddedAddonSubscriptionId = subscriptionId;
            AddedAddonSkus = skus.ToList();
        }

        public Task<Zm.SubscriptionUserModel?> CreateUser(
            string subscriptionId,
            string externalUserId,
            string firstName,
            string lastName,
            string email,
            CancellationToken cancellationToken)
        {
            CreateUserCalls++;
            if (CreateUserException is not null)
            {
                throw CreateUserException;
            }

            CreatedUserSubscriptionId = subscriptionId;
            CreatedExternalUserId = externalUserId;
            var user = new Zm.SubscriptionUserModel
            {
                SubscriptionUserId = "zmsu-demo-user",
                ExternalUserId = externalUserId,
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Status = Zm.SubscriptionUserStatus.Enabled
            };
            _users.Add(user);
            return Task.FromResult<Zm.SubscriptionUserModel?>(
                user);
        }

        public Task<IReadOnlyList<Zm.SubscriptionUserModel>> ListUsers(string subscriptionId,
            CancellationToken cancellationToken)
        {
            ListUsersCalls++;
            return Task.FromResult(
                QueuedUserLists.TryDequeue(out var queued)
                    ? queued
                    : _users.ToList());
        }
    }

    private sealed class StubExternalBillingCheckoutProvider(BillingSystem billingSystem) : IBillingCheckoutProvider
    {
        public BillingSystem BillingSystem => billingSystem;

        public Task<BillingCheckoutResult> CreateCheckout(
            ZenmeterPendingCheckout checkout,
            CancellationToken cancellationToken) =>
            Task.FromResult(BillingCheckoutResult.Pending(
                $"https://checkout.{billingSystem.ToSlug()}.test/session"));
    }

    private sealed class StubFastSpringBillingPaymentVerifier(
        BillingPaymentVerification? verification = null) : IFastSpringBillingPaymentVerifier
    {
        public BillingTopUpPayment? Payment { get; private set; }

        public Task<BillingPaymentVerification> VerifyTopUp(
            BillingTopUpPayment payment,
            CancellationToken cancellationToken)
        {
            Payment = payment;
            return Task.FromResult(verification ?? BillingPaymentVerification.Completed());
        }
    }

    private sealed class StubStripeBillingPaymentVerifier : IStripeBillingPaymentVerifier
    {
        public Task<BillingPaymentVerification> VerifyTopUp(
            BillingTopUpPayment payment,
            CancellationToken cancellationToken) =>
            Task.FromResult(BillingPaymentVerification.Completed());
    }
}
