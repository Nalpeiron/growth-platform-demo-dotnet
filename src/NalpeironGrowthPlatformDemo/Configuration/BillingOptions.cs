using Microsoft.Extensions.Options;

namespace NalpeironGrowthPlatformDemo.Configuration;

public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    public static IReadOnlyList<BillingSystem> DefaultEnabledBillingSystems { get; } =
        [BillingSystem.None, BillingSystem.FastSpring, BillingSystem.Stripe];

    public BillingSystem DefaultBillingSystem { get; set; } = BillingSystem.None;

    public List<BillingSystem> EnabledBillingSystems { get; set; } = [];

    public StripeBillingOptions Stripe { get; set; } = new();

    public FastSpringBillingOptions FastSpring { get; set; } = new();

    public ProvisioningPollOptions ProvisioningPoll { get; set; } = new();
}

public enum BillingSystem
{
    None,
    Stripe,
    FastSpring
}

public static class BillingSystems
{
    public const string DefaultSlug = "default";
    public const string StripeSlug = "stripe";
    public const string FastSpringSlug = "fastspring";

    public static BillingSystem? FromSlug(string? slug) =>
        slug?.Trim().ToLowerInvariant() switch
        {
            DefaultSlug or "none" => BillingSystem.None,
            StripeSlug => BillingSystem.Stripe,
            FastSpringSlug => BillingSystem.FastSpring,
            _ => null
        };

    public static string ToSlug(this BillingSystem billingSystem) =>
        billingSystem switch
        {
            BillingSystem.None => DefaultSlug,
            BillingSystem.Stripe => StripeSlug,
            BillingSystem.FastSpring => FastSpringSlug,
            _ => throw new ArgumentOutOfRangeException(nameof(billingSystem), billingSystem, null)
        };

    public static string DisplayName(this BillingSystem billingSystem) =>
        billingSystem switch
        {
            BillingSystem.None => "None",
            BillingSystem.Stripe => "Stripe",
            BillingSystem.FastSpring => "FastSpring",
            _ => billingSystem.ToString()
        };

    public static bool IsEnabled(this BillingOptions options, BillingSystem billingSystem) =>
        (options.EnabledBillingSystems.Count == 0
            ? BillingOptions.DefaultEnabledBillingSystems
            : options.EnabledBillingSystems).Contains(billingSystem);
}

public sealed class StripeBillingOptions
{
    public string ApiUrl { get; set; } = "https://api.stripe.com";
    public string SecretKey { get; set; } = "";
    public string ZenmeterSuccessUrl { get; set; } = "";
    public string ZenmeterCancelUrl { get; set; } = "";
    public string ZentitleSuccessUrl { get; set; } = "";
    public string ZentitleCancelUrl { get; set; } = "";
}

public sealed class FastSpringBillingOptions
{
    public string ApiUrl { get; set; } = "https://api.fastspring.com";
    public string ZenmeterStorefrontUrl { get; set; } = "";
    public string ZentitleStorefrontUrl { get; set; } = "";
    public string ApiUsername { get; set; } = "";
    public string ApiPassword { get; set; } = "";
}

public sealed class ProvisioningPollOptions
{
    public int IntervalSeconds { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 90;
}

public sealed class BillingOptionsValidator : IValidateOptions<BillingOptions>
{
    public ValidateOptionsResult Validate(string? name, BillingOptions options)
    {
        var failures = new List<string>();

        if (options.EnabledBillingSystems.Distinct().Count() != options.EnabledBillingSystems.Count)
        {
            failures.Add("Billing:EnabledBillingSystems cannot contain duplicate values.");
        }

        if (options.EnabledBillingSystems.Any(billingSystem => !Enum.IsDefined(billingSystem)))
        {
            failures.Add("Billing:EnabledBillingSystems contains an unsupported billing system.");
        }

        if (!options.IsEnabled(options.DefaultBillingSystem))
        {
            failures.Add(
                "The configured default billing system must be included in Billing:EnabledBillingSystems.");
        }

        // Every enabled provider is reachable through its own route (e.g. /elevate/saas/stripe), but
        // only the default provider's settings are required at startup. Enabling a provider before
        // its credentials are filled in is intentional (all routes/buttons stay visible regardless
        // of configuration); visiting that route surfaces a clear on-page error instead of
        // preventing the whole app from starting.
        ValidateProviderSettings(options.DefaultBillingSystem, options, failures);

        if (options.ProvisioningPoll.IntervalSeconds <= 0)
        {
            failures.Add("Billing:ProvisioningPoll:IntervalSeconds must be greater than zero.");
        }

        if (options.ProvisioningPoll.TimeoutSeconds <= 0)
        {
            failures.Add("Billing:ProvisioningPoll:TimeoutSeconds must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateProviderSettings(
        BillingSystem billingSystem,
        BillingOptions options,
        List<string> failures)
    {
        switch (billingSystem)
        {
            case BillingSystem.None:
                break;
            case BillingSystem.Stripe:
                if (!Uri.TryCreate(options.Stripe.ApiUrl, UriKind.Absolute, out _))
                {
                    failures.Add("Billing:Stripe:ApiUrl must be an absolute URL when Stripe billing is enabled.");
                }

                if (string.IsNullOrWhiteSpace(options.Stripe.SecretKey))
                {
                    failures.Add("Billing:Stripe:SecretKey is required when Stripe billing is enabled.");
                }

                break;
            case BillingSystem.FastSpring:
                if (!Uri.TryCreate(options.FastSpring.ApiUrl, UriKind.Absolute, out _))
                {
                    failures.Add(
                        "Billing:FastSpring:ApiUrl must be an absolute URL when FastSpring billing is enabled.");
                }

                if (string.IsNullOrWhiteSpace(options.FastSpring.ApiUsername))
                {
                    failures.Add("Billing:FastSpring:ApiUsername is required when FastSpring billing is enabled.");
                }

                if (string.IsNullOrWhiteSpace(options.FastSpring.ApiPassword))
                {
                    failures.Add("Billing:FastSpring:ApiPassword is required when FastSpring billing is enabled.");
                }

                break;
            default:
                failures.Add($"Billing system '{billingSystem}' is not supported.");
                break;
        }
    }

}
