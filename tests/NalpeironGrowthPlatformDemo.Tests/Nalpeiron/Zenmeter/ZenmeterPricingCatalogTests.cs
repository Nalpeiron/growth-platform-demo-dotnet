using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPriceProviders;
using Zm = NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Nalpeiron.Zenmeter;

public sealed class ZenmeterPricingCatalogTests
{
    [Fact]
    public async Task GetPricing_WithConfiguredSkuPrices_MapsPricingFromTheBusinessModel()
    {
        // arrange
        var client = new StubZenmeterManagementClient
        {
            BusinessModel = BusinessModel()
        };
        var options = new ZenmeterOptions
        {
            ProductName = "Elevate SaaS",
            BusinessModelId = "bm-1",
            Prices =
            {
                ["elevate-saas-scale-monthly"] = new ZenmeterPriceOptions { Price = 149 },
                ["elevate-saas-scale-yearly"] = new ZenmeterPriceOptions { Price = 1490 }
            }
        };
        var catalog = new ZenmeterPricingCatalog(
            client,
            Options.Create(options),
            CreateStaticPriceResolver(options));

        // act
        var pricing = await catalog.GetPricing(CancellationToken.None);

        // assert
        Assert.Equal("bm-1", client.RequestedBusinessModelId);
        Assert.Equal("Elevate SaaS", pricing.ProductName);
        Assert.Equal("credits", pricing.MeterUnitPluralName);
        var tier = Assert.Single(pricing.Tiers);
        Assert.Equal("tier-scale", tier.Key);
        Assert.Equal("Scale", tier.Name);
        Assert.Equal(100000, tier.IncludedMeterAmount);
        Assert.Equal(["AI campaign draft", "Team workspace"], tier.IncludedFeatures);
        Assert.Empty(tier.AddOns);

        Assert.Collection(
            tier.Offerings,
            monthly =>
            {
                Assert.Equal(ZenmeterOfferingPeriod.Monthly, monthly.Period);
                Assert.Equal("elevate-saas-scale-monthly", monthly.Sku);
                Assert.Equal(149, monthly.Price);
                Assert.Equal("per month", monthly.BillingLabel);
            },
            yearly =>
            {
                Assert.Equal(ZenmeterOfferingPeriod.Yearly, yearly.Period);
                Assert.Equal("elevate-saas-scale-yearly", yearly.Sku);
                Assert.Equal(1490, yearly.Price);
                Assert.Equal("per year", yearly.BillingLabel);
            });

        var rate = Assert.Single(pricing.FeatureRates);
        Assert.Equal("ai-campaign-draft", rate.Key);
        Assert.Equal(12, rate.Value.ConversionRate);
        Assert.Equal("credit", rate.Value.MeterUnitName);
        Assert.Equal("credits", rate.Value.MeterUnitPluralName);
    }

    [Fact]
    public async Task GetPricing_WhenOfferingPriceIsMissing_Throws()
    {
        // arrange
        var client = new StubZenmeterManagementClient
        {
            BusinessModel = BusinessModel()
        };
        var options = new ZenmeterOptions
        {
            ProductName = "Elevate SaaS",
            BusinessModelId = "bm-1",
            Prices =
            {
                ["elevate-saas-scale-monthly"] = new ZenmeterPriceOptions { Price = 149 }
            }
        };
        var catalog = new ZenmeterPricingCatalog(
            client,
            Options.Create(options),
            CreateStaticPriceResolver(options));

        // act
        var act = () => catalog.GetPricing(CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("elevate-saas-scale-yearly", exception.Message);
    }

    [Fact]
    public async Task GetPricingShell_WhenCalled_MapsCatalogWithoutResolvingPrices()
    {
        // arrange
        var client = new StubZenmeterManagementClient
        {
            BusinessModel = BusinessModel()
        };
        var options = new ZenmeterOptions
        {
            ProductName = "Elevate SaaS",
            BusinessModelId = "bm-1"
        };
        var catalog = new ZenmeterPricingCatalog(
            client,
            Options.Create(options),
            new ThrowingBillingPriceResolver());

        // act
        var pricing = await catalog.GetPricingShell(CancellationToken.None);

        // assert
        var tier = Assert.Single(pricing.Tiers);
        Assert.Collection(
            tier.Offerings,
            monthly =>
            {
                Assert.Equal("elevate-saas-scale-monthly", monthly.Sku);
                Assert.True(monthly.IsVisible);
                Assert.Equal(0, monthly.Price);
                Assert.Equal("per month", monthly.BillingLabel);
            },
            yearly =>
            {
                Assert.Equal("elevate-saas-scale-yearly", yearly.Sku);
                Assert.True(yearly.IsVisible);
                Assert.Equal(0, yearly.Price);
                Assert.Equal("per year", yearly.BillingLabel);
            });
    }

    [Fact]
    public async Task GetPricing_WhenStripeBillingIsActive_UsesStripeProviderPrices()
    {
        // arrange
        var client = new StubZenmeterManagementClient
        {
            BusinessModel = BusinessModel()
        };
        var options = new ZenmeterOptions
        {
            ProductName = "Elevate SaaS",
            BusinessModelId = "bm-1",
            Prices =
            {
                ["elevate-saas-scale-monthly"] = new ZenmeterPriceOptions { Price = 149 },
                ["elevate-saas-scale-yearly"] = new ZenmeterPriceOptions { Price = 1490 }
            }
        };
        var catalog = new ZenmeterPricingCatalog(
            client,
            Options.Create(options),
            new BillingPriceResolver(
                [
                    new StaticBillingPriceProvider(Options.Create(options)),
                    new StubBillingPriceProvider(
                        BillingSystem.Stripe,
                        new Dictionary<string, BillingPrice>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["elevate-saas-scale-monthly"] = new("elevate-saas-scale-monthly", 222),
                            ["elevate-saas-scale-yearly"] = new("elevate-saas-scale-yearly", 2220)
                        })
                ],
                Options.Create(new BillingOptions { DefaultBillingSystem = BillingSystem.Stripe })));

        // act
        var pricing = await catalog.GetPricing(BillingSystem.Stripe, CancellationToken.None);

        // assert
        var tier = Assert.Single(pricing.Tiers);
        Assert.Collection(
            tier.Offerings,
            monthly =>
            {
                Assert.Equal(222, monthly.Price);
                Assert.True(monthly.IsVisible);
            },
            yearly =>
            {
                Assert.Equal(2220, yearly.Price);
                Assert.True(yearly.IsVisible);
            });
    }

    [Fact]
    public async Task GetCompatibleAddons_WithBaseOfferingSku_MapsAddonsFromTheResponse()
    {
        // arrange
        var client = new StubZenmeterManagementClient
        {
            CompatibleAddons = CompatibleAddons()
        };
        var options = new ZenmeterOptions
        {
            ProductName = "Elevate SaaS",
            BusinessModelId = "bm-1",
            Prices =
            {
                ["elevate-saas-security-suite-1m"] = new ZenmeterPriceOptions { Price = 29 },
                ["elevate-saas-credits-500-monthly"] = new ZenmeterPriceOptions { Price = 10 },
                ["elevate-saas-credits-500-onetime-1m"] = new ZenmeterPriceOptions { Price = 15 }
            }
        };
        var catalog = new ZenmeterPricingCatalog(
            client,
            Options.Create(options),
            CreateStaticPriceResolver(options));

        // act
        var addons = await catalog.GetCompatibleAddons("elevate-saas-scale-monthly", CancellationToken.None);

        // assert
        Assert.Equal("elevate-saas-scale-monthly", client.RequestedBaseOfferingSku);
        Assert.Collection(
            addons,
            security =>
            {
                Assert.Equal("elevate-saas-security-suite-1m", security.Sku);
                Assert.Equal(ZenmeterAddonType.FeatureBundle, security.Type);
                Assert.Equal(ZenmeterRenewalBehavior.OneTime, security.RenewalBehavior);
                Assert.Equal(ZenmeterOfferingPeriod.Any, security.Period);
                Assert.Equal(["Audit logs", "SSO"], security.IncludedFeatures);
                Assert.Equal("one month", security.BillingLabel);
                Assert.Equal(29, security.Price);
            },
            recurringCredits =>
            {
                Assert.Equal("elevate-saas-credits-500-monthly", recurringCredits.Sku);
                Assert.Equal(ZenmeterAddonType.MeterTopUp, recurringCredits.Type);
                Assert.Equal(ZenmeterRenewalBehavior.RenewsWithSubscription, recurringCredits.RenewalBehavior);
                Assert.Equal(ZenmeterOfferingPeriod.Monthly, recurringCredits.Period);
                Assert.Equal(500, recurringCredits.Amount);
                Assert.Equal("per month", recurringCredits.BillingLabel);
                Assert.Equal(10, recurringCredits.Price);
            },
            oneTimeCredits =>
            {
                Assert.Equal("elevate-saas-credits-500-onetime-1m", oneTimeCredits.Sku);
                Assert.Equal(ZenmeterAddonType.MeterTopUp, oneTimeCredits.Type);
                Assert.Equal(ZenmeterRenewalBehavior.OneTime, oneTimeCredits.RenewalBehavior);
                Assert.Equal(ZenmeterOfferingPeriod.Any, oneTimeCredits.Period);
                Assert.Equal(500, oneTimeCredits.Amount);
                Assert.Equal("one month", oneTimeCredits.BillingLabel);
                Assert.Equal(15, oneTimeCredits.Price);
            });
    }

    [Fact]
    public async Task GetCompatibleAddons_WhenAddonPriceIsMissing_Throws()
    {
        // arrange
        var client = new StubZenmeterManagementClient
        {
            CompatibleAddons = CompatibleAddons()
        };
        var options = new ZenmeterOptions
        {
            ProductName = "Elevate SaaS",
            BusinessModelId = "bm-1",
            Prices =
            {
                ["elevate-saas-security-suite-1m"] = new ZenmeterPriceOptions { Price = 29 }
            }
        };
        var catalog = new ZenmeterPricingCatalog(
            client,
            Options.Create(options),
            CreateStaticPriceResolver(options));

        // act
        var act = () => catalog.GetCompatibleAddons("elevate-saas-scale-monthly", CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<BillingPriceException>(act);
        Assert.Contains("elevate-saas-credits-500-monthly", exception.Message);
        Assert.Contains("elevate-saas-credits-500-onetime-1m", exception.Message);
    }

    [Fact]
    public async Task GetCompatibleAddonShell_WhenCalled_MapsAddonsWithoutResolvingPrices()
    {
        // arrange
        var client = new StubZenmeterManagementClient
        {
            CompatibleAddons = CompatibleAddons()
        };
        var options = new ZenmeterOptions
        {
            ProductName = "Elevate SaaS",
            BusinessModelId = "bm-1"
        };
        var catalog = new ZenmeterPricingCatalog(
            client,
            Options.Create(options),
            new ThrowingBillingPriceResolver());

        // act
        var addons = await catalog.GetCompatibleAddonShell("elevate-saas-scale-monthly", CancellationToken.None);

        // assert
        Assert.Collection(
            addons,
            security =>
            {
                Assert.Equal("elevate-saas-security-suite-1m", security.Sku);
                Assert.True(security.IsVisible);
                Assert.Equal(0, security.Price);
                Assert.Equal("one month", security.BillingLabel);
            },
            recurringCredits =>
            {
                Assert.Equal("elevate-saas-credits-500-monthly", recurringCredits.Sku);
                Assert.True(recurringCredits.IsVisible);
                Assert.Equal(0, recurringCredits.Price);
                Assert.Equal("per month", recurringCredits.BillingLabel);
            },
            oneTimeCredits =>
            {
                Assert.Equal("elevate-saas-credits-500-onetime-1m", oneTimeCredits.Sku);
                Assert.True(oneTimeCredits.IsVisible);
                Assert.Equal(0, oneTimeCredits.Price);
                Assert.Equal("one month", oneTimeCredits.BillingLabel);
            });
    }

    private static Zm.CatalogBusinessModelConfigurationModel BusinessModel() =>
        new()
        {
            Id = "bm-1",
            ProductId = "product-1",
            Name = "Subscription pool",
            Rates =
            [
                new Zm.RateEntryModel
                {
                    Rate = 12,
                    ConsumingFeature = new Zm.RateFeatureModel
                    {
                        FeatureReference = FeatureReference("ai-campaign-draft", "AI campaign draft"),
                        Unit = Unit("draft", "drafts")
                    },
                    Meter = new Zm.RateMeterModel
                    {
                        MeterReference = MeterReference("credits", "Credits"),
                        Unit = Unit("credit", "credits")
                    }
                }
            ],
            Tiers =
            [
                new Zm.TierModel
                {
                    Id = "tier-scale",
                    Name = "Scale",
                    Description = "For growth teams.",
                    FeatureLimits =
                    [
                        new Zm.TierFeatureLimitModel
                        {
                            FeatureReference = FeatureReference("ai-campaign-draft", "AI campaign draft"),
                            FeatureKind = Zm.FeatureKind.Quantitative,
                            ConsumptionStrategy = Zm.ConsumptionStrategy.RateBased,
                            Access = Zm.Access.Enabled,
                            Unit = Unit("draft", "drafts")
                        },
                        new Zm.TierFeatureLimitModel
                        {
                            FeatureReference = FeatureReference("team-workspace", "Team workspace"),
                            FeatureKind = Zm.FeatureKind.Access,
                            Access = Zm.Access.Enabled
                        },
                        new Zm.TierFeatureLimitModel
                        {
                            FeatureReference = FeatureReference("sso", "SSO"),
                            FeatureKind = Zm.FeatureKind.Access,
                            Access = Zm.Access.Disabled
                        }
                    ],
                    Meters =
                    [
                        new Zm.TierMeterModel
                        {
                            MeterReference = MeterReference("credits", "Credits"),
                            Unit = Unit("credit", "credits"),
                            UsageGrants = SharedGrant(100000)
                        }
                    ],
                    Offerings =
                    [
                        new Zm.TierOfferingModel
                        {
                            Name = "Scale monthly",
                            Sku = "elevate-saas-scale-monthly",
                            BillingPeriod = Zm.BillingPeriod.Monthly
                        },
                        new Zm.TierOfferingModel
                        {
                            Name = "Scale yearly",
                            Sku = "elevate-saas-scale-yearly",
                            BillingPeriod = Zm.BillingPeriod.Yearly
                        }
                    ]
                }
            ]
        };

    private static Zm.CatalogCompatibleAddonListModel CompatibleAddons() =>
        new()
        {
            BaseOffering = new Zm.CatalogCompatibleBaseOfferingModel
            {
                Sku = "elevate-saas-scale-monthly",
                ProductId = "product-1",
                BusinessModelId = "bm-1",
                TierId = "tier-scale"
            },
            Items =
            [
                new Zm.CatalogCompatibleAddonListItemModel
                {
                    Addon = new Zm.CatalogAddonModel
                    {
                        ProductId = "product-1",
                        Name = "Security Suite",
                        Description = "One-month security features.",
                        FeatureGrants =
                        [
                            new Zm.CatalogAddonFeatureGrantModel
                            {
                                FeatureReference = FeatureReference("audit-logs", "Audit logs"),
                                FeatureKind = Zm.FeatureKind.Access
                            },
                            new Zm.CatalogAddonFeatureGrantModel
                            {
                                FeatureReference = FeatureReference("sso", "SSO"),
                                FeatureKind = Zm.FeatureKind.Access
                            }
                        ],
                        MeterGrants = []
                    },
                    Offerings =
                    [
                        new Zm.CatalogAddonOfferingModel
                        {
                            Name = "Security Suite",
                            Sku = "elevate-saas-security-suite-1m",
                            Term = new Zm.AddonOfferingTermModel
                            {
                                RenewalBehavior = Zm.AddonRenewalBehavior.OneTime,
                                Duration = Interval(Zm.IntervalType.Month, 1)
                            }
                        }
                    ]
                },
                new Zm.CatalogCompatibleAddonListItemModel
                {
                    Addon = new Zm.CatalogAddonModel
                    {
                        ProductId = "product-1",
                        Name = "500 credits",
                        Description = "Credit packs.",
                        FeatureGrants = [],
                        MeterGrants =
                        [
                            new Zm.CatalogAddonMeterGrantModel
                            {
                                MeterReference = MeterReference("credits", "Credits"),
                                Unit = Unit("credit", "credits"),
                                UsageGrants = Grant(500)
                            }
                        ]
                    },
                    Offerings =
                    [
                        new Zm.CatalogAddonOfferingModel
                        {
                            Name = "500 credits / month",
                            Sku = "elevate-saas-credits-500-monthly",
                            Term = new Zm.AddonOfferingTermModel
                            {
                                RenewalBehavior = Zm.AddonRenewalBehavior.RenewsWithSubscription,
                                BillingPeriod = Zm.BillingPeriod.Monthly
                            }
                        },
                        new Zm.CatalogAddonOfferingModel
                        {
                            Name = "Extra 500 credits",
                            Sku = "elevate-saas-credits-500-onetime-1m",
                            Term = new Zm.AddonOfferingTermModel
                            {
                                RenewalBehavior = Zm.AddonRenewalBehavior.OneTime,
                                Duration = Interval(Zm.IntervalType.Month, 1)
                            }
                        }
                    ]
                }
            ]
        };

    private static Zm.UnitModel Unit(string name, string pluralName) =>
        new() { Name = name, PluralName = pluralName };

    private static Zm.FeatureReferenceModel FeatureReference(string key, string displayName) =>
        new() { Key = key, DisplayName = displayName };

    private static Zm.MeterReferenceModel MeterReference(string key, string displayName) =>
        new() { Key = key, DisplayName = displayName };

    private static Zm.ScopedUsageGrantsModel SharedGrant(long includedAmount) =>
        new() { Shared = Grant(includedAmount) };

    private static Zm.UsageGrantModel Grant(long includedAmount) =>
        new() { IncludedAmount = includedAmount };

    private static Zm.Interval Interval(Zm.IntervalType type, int count) =>
        new() { Type = type, Count = count };

    private static IBillingPriceResolver CreateStaticPriceResolver(ZenmeterOptions options) =>
        new BillingPriceResolver(
            [new StaticBillingPriceProvider(Options.Create(options))],
            Options.Create(new BillingOptions { DefaultBillingSystem = BillingSystem.None }));

    private sealed class StubBillingPriceProvider(
        BillingSystem billingSystem,
        IReadOnlyDictionary<string, BillingPrice> prices) : IBillingPriceProvider
    {
        public BillingSystem BillingSystem { get; } = billingSystem;

        public Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
            IReadOnlyCollection<string> skus,
            CancellationToken cancellationToken) =>
            Task.FromResult(prices);
    }

    private sealed class ThrowingBillingPriceResolver : IBillingPriceResolver
    {
        public Task<IReadOnlyDictionary<string, BillingPrice>> GetPrices(
            BillingSystem billingSystem,
            IReadOnlyCollection<string> skus,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Prices should not be resolved.");

        public Task<IReadOnlyDictionary<string, BillingPrice>?> TryGetPriceBook(
            BillingSystem billingSystem,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Prices should not be resolved.");
    }

    private sealed class StubZenmeterManagementClient : IZenmeterManagementClient
    {
        public Zm.CatalogBusinessModelConfigurationModel? BusinessModel { get; init; }
        public Zm.CatalogCompatibleAddonListModel? CompatibleAddons { get; init; }
        public string? RequestedBusinessModelId { get; private set; }
        public string? RequestedBaseOfferingSku { get; private set; }

        public Task<Zm.CatalogBusinessModelConfigurationModel?> GetBusinessModel(
            string businessModelId,
            CancellationToken cancellationToken)
        {
            RequestedBusinessModelId = businessModelId;
            return Task.FromResult(BusinessModel);
        }

        public Task<Zm.CatalogCompatibleAddonListModel?> GetCompatibleAddons(
            string baseOfferingSku,
            CancellationToken cancellationToken)
        {
            RequestedBaseOfferingSku = baseOfferingSku;
            return Task.FromResult(CompatibleAddons);
        }

        public Task<Zm.SubscriptionModel?> CreateSubscription(
            string customerId,
            IReadOnlyList<string> skus,
            string orderRefId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zm.SubscriptionModel?> GetSubscription(
            string subscriptionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zm.SubscriptionModel?> LookupSubscription(
            string? orderRefId,
            string? subscriptionRefId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Zm.SubscriptionFeatureListItemModel>> GetFeatures(
            string subscriptionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Zm.SubscriptionMeterListItemModel>> GetMeters(
            string subscriptionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAddons(
            string subscriptionId,
            IReadOnlyList<string> skus,
            string? orderRefId,
            BillingSystem? billingSystem,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Zm.SubscriptionUserModel?> CreateUser(
            string subscriptionId,
            string externalUserId,
            string firstName,
            string lastName,
            string email,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Zm.SubscriptionUserModel>> ListUsers(
            string subscriptionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
