using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Application.Zentitle;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zentitle;

public sealed class ElevateDemoServiceTests
{
    [Fact]
    public async Task Purchase_WithEntitlementGroupWithoutAnId_RejectsThePurchase()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient
        {
            Group = Group(null)
        };
        var service = CreateService(Plan(isPriceConfigured: true), zentitle, out var customers);

        // act
        var result = await service.Purchase(BillingSystem.None, "off-1", "Acme", "checkout-1", CancellationToken.None);

        // assert
        Assert.Null(result.SessionId);
        Assert.Equal(
            "Zentitle could not complete the purchase. Return to pricing and try again.",
            result.Error);
        Assert.DoesNotContain("customer-1", result.Error);
        Assert.DoesNotContain("reviewed manually", result.Error);
        Assert.Equal(1, customers.CreateCalls);
    }

    [Fact]
    public async Task Purchase_WithEntitlementGroupWithoutAnEntitlement_RejectsThePurchase()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient
        {
            Group = Group("group-1", [])
        };
        var service = CreateService(Plan(isPriceConfigured: true), zentitle, out _);

        // act
        var result = await service.Purchase(BillingSystem.None, "off-1", "Acme", "checkout-2", CancellationToken.None);

        // assert
        Assert.Null(result.SessionId);
        Assert.Equal(
            "Zentitle could not complete the purchase. Return to pricing and try again.",
            result.Error);
        Assert.DoesNotContain("group-1", result.Error);
    }

    [Fact]
    public async Task Purchase_WhenPriceIsMissing_DoesNotCreateACustomer()
    {
        // arrange
        var service = CreateService(Plan(isPriceConfigured: false), new StubZentitleManagementClient(),
            out var customers);

        // act
        var result = await service.Purchase(BillingSystem.None, "off-1", "Acme", "checkout-3", CancellationToken.None);

        // assert
        Assert.Null(result.SessionId);
        Assert.Contains("no price is configured", result.Error);
        Assert.Equal(0, customers.CreateCalls);
    }

    [Fact]
    public async Task Purchase_WithDuplicateCheckoutRequestId_RejectsTheSecondSubmission()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient
        {
            Group = Group("group-1")
        };
        var service = CreateService(Plan(isPriceConfigured: true), zentitle, out var customers);

        // act
        var first = await service.Purchase(BillingSystem.None, "off-1", "Acme", "checkout-duplicate",
            CancellationToken.None);
        var second = await service.Purchase(BillingSystem.None, "off-1", "Acme", "checkout-duplicate",
            CancellationToken.None);

        // assert
        Assert.NotNull(first.SessionId);
        Assert.Null(second.SessionId);
        Assert.Contains("already submitted", second.Error);
        Assert.Equal(1, customers.CreateCalls);
    }

    [Fact]
    public async Task GetWorkspace_WithConfiguredUiBaseUrl_BuildsZentitleDeepLinks()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient
        {
            Group = Group("group-1"),
            Entitlement = Entitlement("ent-1")
        };
        var service = CreateService(
            Plan(isPriceConfigured: true),
            zentitle,
            out _,
            webUrl: "https://tenant-name.nalpeiron.io/zenmeter/");

        // act
        var purchase =
            await service.Purchase(BillingSystem.None, "off-1", "Acme", "checkout-links", CancellationToken.None);
        var workspace = await service.GetWorkspace(purchase.SessionId!, CancellationToken.None);

        // assert
        Assert.Equal("https://tenant-name.nalpeiron.io/zentitle/customers/customer-1", workspace!.CustomerUrl);
        Assert.Equal("https://tenant-name.nalpeiron.io/zentitle/entitlements/ent-1#details",
            workspace.EntitlementUrl);
        Assert.Equal("https://tenant-name.nalpeiron.io/zentitle/entitlements/ent-1#activity-log",
            workspace.ActivityLogUrl);
    }

    [Fact]
    public async Task CheckoutFeature_WithEntitlementUsage_PrefersItOverTheActivationActiveCount()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient
        {
            Group = Group("group-1"),
            Activation = new Zt.ActivationStateModel { Id = "activation-1" },
            Feature = new Zt.ActivationFeatureModel
            {
                Key = "exports",
                Type = Zt.FeatureType.ElementPool,
                Total = 100,
                Active = 10,
                Available = 75
            },
            Entitlement = Entitlement(
                "ent-1",
                [EntitlementFeature("exports", Zt.FeatureType.ElementPool, value: 100, used: 25)])
        };
        var service = CreateService(Plan(isPriceConfigured: true), zentitle, out _);
        var purchase = await service.Purchase(BillingSystem.None, "off-1", "Acme", "checkout-feature",
            CancellationToken.None);

        // act
        var result = await service.CheckoutFeature(
            purchase.SessionId!,
            "exports",
            25,
            CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal("activation-1", result.ActivationId);
        Assert.Equal(100, result.Feature!.Value);
        Assert.Equal(25, result.Feature.Used);
        Assert.Equal(1, zentitle.GetEntitlementCalls);
    }

    [Fact]
    public async Task CheckoutFeature_WhenActivationActiveIsNull_RefreshesTheUsageCount()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient
        {
            Group = Group("group-1"),
            Activation = new Zt.ActivationStateModel { Id = "activation-1" },
            Feature = new Zt.ActivationFeatureModel
            {
                Key = "exports",
                Type = Zt.FeatureType.UsageCount,
                Total = 100,
                Active = null,
                Available = 75
            },
            Entitlement = Entitlement(
                "ent-1",
                [EntitlementFeature("exports", Zt.FeatureType.UsageCount, value: 100, used: 25)])
        };
        var service = CreateService(Plan(isPriceConfigured: true), zentitle, out _);
        var purchase = await service.Purchase(
            BillingSystem.None,
            "off-1",
            "Acme",
            "checkout-usage-count",
            CancellationToken.None);

        // act
        var result = await service.CheckoutFeature(
            purchase.SessionId!,
            "exports",
            25,
            CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal(100, result.Feature!.Value);
        Assert.Equal(25, result.Feature.Used);
        Assert.Equal(1, zentitle.GetEntitlementCalls);
    }

    [Fact]
    public async Task CheckoutFeature_WhenOverdraftChangesTheAvailableBalance_UsesAuthoritativeUsage()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient
        {
            Group = Group("group-1"),
            Activation = new Zt.ActivationStateModel { Id = "activation-1" },
            Feature = new Zt.ActivationFeatureModel
            {
                Key = "exports",
                Type = Zt.FeatureType.UsageCount,
                Total = 100,
                Active = null,
                Available = 95
            },
            Entitlement = Entitlement(
                "ent-1",
                [EntitlementFeature("exports", Zt.FeatureType.UsageCount, value: 100, used: 25)])
        };
        var service = CreateService(Plan(isPriceConfigured: true), zentitle, out _);
        var purchase = await service.Purchase(
            BillingSystem.None,
            "off-1",
            "Acme",
            "checkout-overdraft",
            CancellationToken.None);

        // act
        var result = await service.CheckoutFeature(
            purchase.SessionId!,
            "exports",
            25,
            CancellationToken.None);

        // assert
        Assert.True(result.Succeeded);
        Assert.Equal(25, result.Feature!.Used);
    }

    [Fact]
    public async Task CheckoutFeature_WhenApiReturnsPaymentRequired_MapsItToInsufficientBalance()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient
        {
            Group = Group("group-1"),
            Activation = new Zt.ActivationStateModel { Id = "activation-1" },
            FeatureException = new Zt.ZentitleManagementApiException(
                "Payment required",
                statusCode: 402,
                response: null,
                headers: new Dictionary<string, IEnumerable<string>>(),
                innerException: null),
            Entitlement = Entitlement(
                "ent-1",
                [EntitlementFeature("exports", Zt.FeatureType.UsageCount, value: 100, used: 100)])
        };
        var service = CreateService(Plan(isPriceConfigured: true), zentitle, out _);
        var purchase = await service.Purchase(
            BillingSystem.None,
            "off-1",
            "Acme",
            "checkout-usage-limit",
            CancellationToken.None);

        // act
        var result = await service.CheckoutFeature(
            purchase.SessionId!,
            "exports",
            1,
            CancellationToken.None);

        // assert
        Assert.False(result.Succeeded);
        Assert.Equal("insufficient_balance", result.Code);
        Assert.Contains("usage limit has been reached", result.Message);
        Assert.Equal(100, result.Feature!.Used);
    }

    [Fact]
    public async Task Purchase_WithFastSpring_StoresPendingSessionWithoutCreatingTheGroup()
    {
        // arrange
        var zentitle = new StubZentitleManagementClient();
        var service = CreateService(
            Plan(isPriceConfigured: true),
            zentitle,
            out var customers,
            out var store);

        // act
        var purchase = await service.Purchase(
            BillingSystem.FastSpring,
            "off-1",
            "Acme",
            "checkout-fastspring",
            CancellationToken.None);

        // assert
        Assert.Null(purchase.Error);
        Assert.Contains("/elevate/billing/fastspring-popup", purchase.RedirectUrl);
        var session = Assert.IsType<ElevateSession>(store.Get(purchase.SessionId!));
        Assert.Equal(BillingSystem.FastSpring, session.BillingSystem);
        Assert.Equal(ZentitleCheckoutStatuses.Pending, session.CheckoutStatus);
        Assert.Equal("account-ref-1", session.CustomerAccountRefId);
        Assert.Equal(1, customers.CreateCalls);
        Assert.Equal(0, zentitle.CreateGroupCalls);
    }

    [Fact]
    public async Task Purchase_WhenExternalCallFails_ReturnsMessageWithoutApiDetail()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true),
            new StubZentitleManagementClient(),
            out _,
            customerFailure: new Zt.ZentitleManagementApiException(
                "Customer create rejected by tenant policy tnt_7f3a on node api-03.",
                statusCode: 500,
                response: null,
                headers: new Dictionary<string, IEnumerable<string>>(),
                innerException: null));

        // act
        var result = await service.Purchase(
            BillingSystem.None,
            "off-1",
            "Acme",
            "checkout-external-failure",
            CancellationToken.None);

        // assert
        Assert.Null(result.SessionId);
        Assert.DoesNotContain("tnt_7f3a", result.Error);
        Assert.DoesNotContain("api-03", result.Error);
        Assert.Contains("Return to pricing", result.Error);
    }

    [Fact]
    public async Task Purchase_WhenTechnicalExceptionIsThrown_ReturnsSafeMessage()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true),
            new StubZentitleManagementClient(),
            out _,
            customerFailure: new InvalidOperationException(
                "Customer response for tenant tnt_7f3a did not contain an id on node api-03."));

        // act
        var result = await service.Purchase(
            BillingSystem.None,
            "off-1",
            "Acme",
            "checkout-invalid-operation-failure",
            CancellationToken.None);

        // assert
        Assert.Null(result.SessionId);
        Assert.DoesNotContain("tnt_7f3a", result.Error);
        Assert.DoesNotContain("api-03", result.Error);
        Assert.Equal(
            "Zentitle could not complete the purchase. Return to pricing and try again.",
            result.Error);
    }

    [Fact]
    public async Task Purchase_WithFastSpringAndMissingZentitleStorefront_DoesNotCreateACustomer()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true),
            new StubZentitleManagementClient(),
            out var customers,
            zentitleStorefrontUrl: "");

        // act
        var purchase = await service.Purchase(
            BillingSystem.FastSpring,
            "off-1",
            "Acme",
            "checkout-fastspring-missing-storefront",
            CancellationToken.None);

        // assert
        Assert.Null(purchase.SessionId);
        Assert.Contains("ZentitleStorefrontUrl is required", purchase.Error);
        Assert.Equal(0, customers.CreateCalls);
    }

    [Fact]
    public async Task GetCheckoutInfo_WithUnconfiguredProvider_ReturnsConfigurationErrorBeforeLoadingPricing()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true),
            new StubZentitleManagementClient(),
            out _,
            zentitleStorefrontUrl: "",
            pricingFailure: new InvalidOperationException("Pricing should not be requested."));

        // act
        var checkout = await service.GetCheckoutInfo(
            BillingSystem.FastSpring,
            "off-1",
            CancellationToken.None);

        // assert
        Assert.NotNull(checkout);
        Assert.False(checkout.CanPurchase);
        Assert.Contains("ZentitleStorefrontUrl is required", checkout.UnavailableReason);
        Assert.DoesNotContain("Pricing should not be requested", checkout.UnavailableReason);
    }

    [Fact]
    public async Task GetPricing_WithUnconfiguredProvider_ThrowsConfigurationErrorBeforeLoadingTheCatalogue()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true),
            new StubZentitleManagementClient(),
            out _,
            zentitleStorefrontUrl: "",
            pricingFailure: new InvalidOperationException("Pricing should not be requested."));

        // act
        var act = () => service.GetPricing(BillingSystem.FastSpring, CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("ZentitleStorefrontUrl is required", exception.Message);
        Assert.DoesNotContain("Pricing should not be requested", exception.Message);
    }

    [Fact]
    public async Task Purchase_WithFastSpringWhenPricingLookupFails_ReturnsControlledError()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true),
            new StubZentitleManagementClient(),
            out var customers,
            pricingFailure: new HttpRequestException("FastSpring unavailable"));

        // act
        var purchase = await service.Purchase(
            BillingSystem.FastSpring,
            "off-1",
            "Acme",
            "checkout-fastspring-pricing-error",
            CancellationToken.None);

        // assert
        Assert.Null(purchase.SessionId);
        Assert.Contains("pricing is temporarily unavailable", purchase.Error);
        Assert.Equal(0, customers.CreateCalls);
    }

    [Fact]
    public async Task Purchase_WithFastSpringPerpetualOffering_RejectsItBeforeCreatingACustomer()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true, period: BillingPeriod.Perpetual),
            new StubZentitleManagementClient(),
            out var customers);

        // act
        var purchase = await service.Purchase(
            BillingSystem.FastSpring,
            "off-1",
            "Acme",
            "checkout-fastspring-perpetual",
            CancellationToken.None);

        // assert
        Assert.Null(purchase.SessionId);
        Assert.Contains("does not support perpetual Zentitle licenses", purchase.Error);
        Assert.Equal(0, customers.CreateCalls);
    }

    [Fact]
    public async Task Purchase_WithFastSpringTrialOffering_RejectsItBeforeCreatingACustomer()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true, period: BillingPeriod.Trial, isTrial: true),
            new StubZentitleManagementClient(),
            out var customers);

        // act
        var purchase = await service.Purchase(
            BillingSystem.FastSpring,
            "off-1",
            "Acme",
            "checkout-fastspring-trial",
            CancellationToken.None);

        // assert
        Assert.Null(purchase.SessionId);
        Assert.Contains("Free trials use the standard Zentitle checkout", purchase.Error);
        Assert.Equal(0, customers.CreateCalls);
    }

    [Fact]
    public async Task Upgrade_WithFastSpringManagedSession_IsDisabled()
    {
        // arrange
        var service = CreateService(
            Plan(isPriceConfigured: true),
            new StubZentitleManagementClient(),
            out _,
            out var store);
        var purchase = await service.Purchase(
            BillingSystem.FastSpring,
            "off-1",
            "Acme",
            "checkout-fastspring-upgrade",
            CancellationToken.None);
        var session = store.Get(purchase.SessionId!)!;
        session.EntitlementId = "entitlement-1";

        // act
        var result = await service.Upgrade(session.SessionId, CancellationToken.None);

        // assert
        Assert.False(result.Succeeded);
        Assert.Equal("upgrade_unavailable", result.Code);
        Assert.Contains("FastSpring-managed", result.Message);
    }

    private static ElevateDemoService CreateService(
        OfferingPlanPricing plan,
        StubZentitleManagementClient zentitle,
        out StubCustomersClient customers,
        string webUrl = "",
        string zentitleStorefrontUrl = "store.test/popup-zentitle",
        Exception? pricingFailure = null,
        Exception? customerFailure = null)
    {
        return CreateService(
            plan,
            zentitle,
            out customers,
            out _,
            webUrl,
            zentitleStorefrontUrl,
            pricingFailure,
            customerFailure);
    }

    private static ElevateDemoService CreateService(
        OfferingPlanPricing plan,
        StubZentitleManagementClient zentitle,
        out StubCustomersClient customers,
        out InMemoryElevateSessionStore store,
        string webUrl = "",
        string zentitleStorefrontUrl = "store.test/popup-zentitle",
        Exception? pricingFailure = null,
        Exception? customerFailure = null)
    {
        customers = new StubCustomersClient(customerFailure);
        var edition = new EditionPricing("edition-1", "Standard", "", [plan], []);
        store = new InMemoryElevateSessionStore();
        var billingOptions = Options.Create(new BillingOptions
        {
            FastSpring = new FastSpringBillingOptions
            {
                ZentitleStorefrontUrl = zentitleStorefrontUrl
            }
        });
        var billingProviders = new ZentitleBillingProviderRegistry(
            [
                new DefaultZentitleBillingProvider(zentitle),
                new FastSpringZentitleBillingProvider(billingOptions, zentitle)
            ],
            billingOptions);
        var billingStatus = new ZentitleBillingStatusService(
            billingProviders,
            store,
            billingOptions,
            NullLogger<ZentitleBillingStatusService>.Instance);
        return new ElevateDemoService(
            new StubPricingCatalog([edition], pricingFailure),
            customers,
            zentitle,
            store,
            new MemoryCacheCheckoutRequestGuard(new MemoryCache(new MemoryCacheOptions())),
            billingProviders,
            billingStatus,
            Options.Create(new NalpeironOptions { WebUrl = webUrl }),
            Options.Create(new ZentitleOptions { ProductId = "product-1" }),
            NullLogger<ElevateDemoService>.Instance);
    }

    private static OfferingPlanPricing Plan(
        bool isPriceConfigured,
        BillingPeriod period = BillingPeriod.Yearly,
        bool isTrial = false) =>
        new(
            "off-1",
            "sku-1",
            period,
            IsTrial: isTrial,
            isPriceConfigured,
            Price: 499,
            BillingLabel: "billed yearly");

    private static Zt.EntitlementGroupModel Group(
        string? id,
        IReadOnlyList<Zt.EntitlementGroupEntitlementModel>? entitlements = null) =>
        new()
        {
            Id = id!,
            Entitlements = entitlements?.ToList()
                           ??
                           [
                               new Zt.EntitlementGroupEntitlementModel
                               {
                                   Id = "ent-1",
                                   Sku = "sku-1",
                                   OfferingId = "off-1",
                                   OfferingName = "Standard",
                                   ProductId = "product-1",
                                   Status = Zt.EntitlementStatus.Active
                               }
                           ],
            ActivationCodes = ["activation-code"]
        };

    private static Zt.EntitlementModel Entitlement(
        string id,
        IReadOnlyList<Zt.EntitlementFeatureModel>? features = null) =>
        new()
        {
            Id = id,
            Sku = "sku-1",
            OfferingName = "Standard",
            PlanName = "Yearly",
            PlanType = Zt.PlanType.Paid,
            LicenseType = Zt.LicenseType.Subscription,
            OfferingId = "off-1",
            EntitlementGroupId = "group-1",
            ProductId = "product-1",
            Status = Zt.EntitlementStatus.Active,
            Features = features?.ToList() ?? []
        };

    private static Zt.EntitlementFeatureModel EntitlementFeature(
        string key,
        Zt.FeatureType type,
        long value,
        long used) =>
        new()
        {
            Key = key,
            Type = type,
            Value = value,
            Used = used
        };

    private sealed class StubPricingCatalog(
        IReadOnlyList<EditionPricing> pricing,
        Exception? failure = null) : IPricingCatalog
    {
        public Task<IReadOnlyList<EditionPricing>> GetPricing(CancellationToken cancellationToken) =>
            failure is null
                ? Task.FromResult(pricing)
                : Task.FromException<IReadOnlyList<EditionPricing>>(failure);

        public Task<IReadOnlyList<EditionPricing>> GetPricing(
            BillingSystem billingSystem,
            CancellationToken cancellationToken) =>
            failure is null
                ? Task.FromResult(pricing)
                : Task.FromException<IReadOnlyList<EditionPricing>>(failure);
    }

    private sealed class StubCustomersClient(Exception? failure = null) : ICustomersClient
    {
        public int CreateCalls { get; private set; }

        public Task<CustomerRef> CreateCustomer(string name, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return failure is null
                ? Task.FromResult(new CustomerRef("customer-1", "account-ref-1"))
                : Task.FromException<CustomerRef>(failure);
        }
    }

    private sealed class StubZentitleManagementClient : IZentitleManagementClient
    {
        public Zt.EntitlementGroupModel? Group { get; init; }
        public Zt.EntitlementModel? Entitlement { get; init; }
        public Zt.ActivationStateModel? Activation { get; init; }
        public Zt.ActivationFeatureModel? Feature { get; init; }
        public Exception? FeatureException { get; init; }
        public int GetEntitlementCalls { get; private set; }
        public int CreateGroupCalls { get; private set; }

        public Task<IReadOnlyList<Zt.OfferingListModel>> GetOfferings(
            string productId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Zt.FeatureModel>> GetEditionFeatures(
            string productId,
            string editionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zt.EntitlementGroupModel?> CreateGroup(
            string customerId,
            string sku,
            string orderRefId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Group);

        public Task<Zt.EntitlementGroupModel?> GetGroup(
            string entitlementGroupId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Group);

        public Task<Zt.EntitlementGroupModel?> LookupGroup(
            string customerId,
            string orderRefId,
            CancellationToken cancellationToken)
        {
            CreateGroupCalls++;
            return Task.FromResult(Group);
        }

        public Task<Zt.EntitlementModel?> GetEntitlement(
            string entitlementId,
            CancellationToken cancellationToken)
        {
            GetEntitlementCalls++;
            return Task.FromResult(Entitlement);
        }

        public Task ChangeOffering(
            string entitlementId,
            string offeringId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zt.ActivationStateModel?> CreateActivation(
            string productId,
            string activationCode,
            string seatId,
            string seatName,
            string? editionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Activation);

        public Task<Zt.ActivationFeatureModel?> CheckoutFeature(
            string activationId,
            string featureKey,
            long amount,
            CancellationToken cancellationToken) =>
            FeatureException is null
                ? Task.FromResult(Feature)
                : Task.FromException<Zt.ActivationFeatureModel?>(FeatureException);

        public Task<Zt.ActivationFeatureModel?> ReturnFeature(
            string activationId,
            string featureKey,
            long amount,
            CancellationToken cancellationToken) =>
            Task.FromResult(Feature);
    }
}
