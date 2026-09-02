using NalpeironGrowthPlatformDemo.Configuration;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Configuration;

public sealed class BillingOptionsValidatorTests
{
    private readonly BillingOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenOnlyDefaultProviderConfigured_SucceedsForNoneOnly()
    {
        // arrange
        var options = new BillingOptions
        {
            DefaultBillingSystem = BillingSystem.None,
            EnabledBillingSystems = [BillingSystem.None]
        };

        // act
        var result = _validator.Validate(null, options);

        // assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenDefaultProviderIsMisconfigured_Fails()
    {
        // arrange
        var options = new BillingOptions
        {
            DefaultBillingSystem = BillingSystem.Stripe,
            EnabledBillingSystems = [BillingSystem.None, BillingSystem.Stripe],
            Stripe = new StripeBillingOptions { SecretKey = "" }
        };

        // act
        var result = _validator.Validate(null, options);

        // assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures ?? [], failure => failure.Contains("Billing:Stripe:SecretKey"));
    }

    [Fact]
    public void Validate_WhenNonDefaultEnabledProviderIsMisconfigured_StillSucceeds()
    {
        // arrange
        // All three providers stay enabled (buttons/routes always visible) even if Stripe and
        // FastSpring have no credentials yet - the app must still start. Visiting /elevate/saas/stripe
        // or /elevate/saas/fastspring in that state surfaces a clear on-page error instead
        // (see PricingConfigurator/Checkout error handling), it doesn't block startup.
        var options = new BillingOptions
        {
            DefaultBillingSystem = BillingSystem.None,
            EnabledBillingSystems = [BillingSystem.None, BillingSystem.Stripe, BillingSystem.FastSpring],
            Stripe = new StripeBillingOptions { SecretKey = "" },
            FastSpring = new FastSpringBillingOptions
            {
                ZenmeterStorefrontUrl = "",
                ZentitleStorefrontUrl = "",
                ApiUsername = "",
                ApiPassword = ""
            }
        };

        // act
        var result = _validator.Validate(null, options);

        // assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenDefaultBillingSystemNotEnabled_Fails()
    {
        // arrange
        var options = new BillingOptions
        {
            DefaultBillingSystem = BillingSystem.Stripe,
            EnabledBillingSystems = [BillingSystem.None]
        };

        // act
        var result = _validator.Validate(null, options);

        // assert
        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains("default billing system must be included"));
    }

    [Fact]
    public void Validate_WhenEnabledBillingSystemsHasDuplicates_Fails()
    {
        // arrange
        var options = new BillingOptions
        {
            DefaultBillingSystem = BillingSystem.None,
            EnabledBillingSystems = [BillingSystem.None, BillingSystem.None]
        };

        // act
        var result = _validator.Validate(null, options);

        // assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures ?? [], failure => failure.Contains("duplicate"));
    }

    [Fact]
    public void Validate_WhenEnabledBillingSystemsContainsUndefinedValue_Fails()
    {
        // arrange
        var options = new BillingOptions
        {
            DefaultBillingSystem = BillingSystem.None,
            EnabledBillingSystems = [BillingSystem.None, (BillingSystem)99]
        };

        // act
        var result = _validator.Validate(null, options);

        // assert
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures ?? [], failure => failure.Contains("unsupported billing system"));
    }

    [Fact]
    public void Validate_WhenDefaultProviderFullyConfigured_Succeeds()
    {
        // arrange
        var options = new BillingOptions
        {
            DefaultBillingSystem = BillingSystem.Stripe,
            EnabledBillingSystems = [BillingSystem.None, BillingSystem.Stripe, BillingSystem.FastSpring],
            Stripe = new StripeBillingOptions
            {
                ApiUrl = "https://api.stripe.com",
                SecretKey = "sk_test_xxx"
            }
        };

        // act
        var result = _validator.Validate(null, options);

        // assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenFastSpringIsDefault_DoesNotRequireAStorefrontForPriceLookup()
    {
        // arrange
        var options = new BillingOptions
        {
            DefaultBillingSystem = BillingSystem.FastSpring,
            EnabledBillingSystems = [BillingSystem.None, BillingSystem.FastSpring],
            FastSpring = new FastSpringBillingOptions
            {
                ApiUrl = "https://api.fastspring.com",
                ZentitleStorefrontUrl = "store.test/popup-zentitle",
                ApiUsername = "user",
                ApiPassword = "password"
            }
        };

        // act
        var result = _validator.Validate(null, options);

        // assert
        Assert.True(result.Succeeded);
    }
}
