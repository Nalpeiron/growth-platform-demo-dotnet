using System.Globalization;
using System.Text.Json;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Configuration;
using static NalpeironGrowthPlatformDemo.Application.Zenmeter.JsonElementHelpers;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public interface IZenmeterTrialBilling
{
    Task<string?> StartConversion(ZenmeterDemoSession session, CancellationToken cancellationToken);
    Task<string?> GetPaymentUrl(ZenmeterDemoSession session, CancellationToken cancellationToken);
}

public sealed class TrialConversionRejectedException(string message) : Exception(message);

public sealed class ZenmeterTrialBilling(
    StripeTrialBilling stripe,
    IFastSpringBillingApiClient fastSpring) : IZenmeterTrialBilling
{
    public async Task<string?> StartConversion(ZenmeterDemoSession session, CancellationToken cancellationToken)
    {
        if (session.BillingSystem == BillingSystem.Stripe)
            return await stripe.Start(session, cancellationToken);

        if (session.BillingSystem != BillingSystem.FastSpring)
            throw new TrialConversionRejectedException("Unsupported billing provider.");

        // The buyer chooses Pay now in the official account portal. Generating its
        // authenticated link must not reschedule billing or charge the customer.
        return await GetPaymentUrl(session, cancellationToken);
    }

    public async Task<string?> GetPaymentUrl(ZenmeterDemoSession session, CancellationToken cancellationToken)
    {
        if (session.BillingSystem == BillingSystem.FastSpring)
            return await GetFastSpringPaymentUrl(session, cancellationToken);
        if (session.BillingSystem != BillingSystem.Stripe)
            return null;

        return await stripe.GetPaymentUrl(session, cancellationToken);
    }

    private async Task<string?> GetFastSpringPaymentUrl(ZenmeterDemoSession session, CancellationToken cancellationToken)
    {
        using var subscription = await GetFastSpringSubscription(session, cancellationToken);
        var root = subscription.Payload!.RootElement;
        var state = FirstStringProperty(root, "state");
        if (state is "active" or "overdue")
            return null;
        if (state != "trial")
            throw new TrialConversionRejectedException("This FastSpring subscription is no longer an active trial.");
        var account = FirstStringProperty(root, "account");
        if (string.IsNullOrWhiteSpace(account))
            throw new TrialConversionRejectedException("The FastSpring billing account is unavailable. Please try again.");

        if (session.TrialConversionPaymentUrl is { } cached
            && session.TrialConversionPaymentUrlExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            session.TrialConversionPaymentUrl = new UriBuilder(cached) { Fragment = "/trials" }.Uri.AbsoluteUri;
            return session.TrialConversionPaymentUrl;
        }

        try
        {
            using var response = await fastSpring.GetAccountManagementUrl(account, cancellationToken);
            if (response.IsSuccessStatusCode && response.Payload is not null
                && TryGetProperty(response.Payload.RootElement, "accounts", out var accounts)
                && accounts.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in accounts.EnumerateArray())
                {
                    if (FirstStringProperty(item, "account") != account
                        || FirstStringProperty(item, "result") != "success"
                        || !Uri.TryCreate(FirstStringProperty(item, "url"), UriKind.Absolute, out var uri)
                        || uri.Scheme != "https" || uri.UserInfo.Length != 0
                        || !uri.AbsolutePath.StartsWith("/account/", StringComparison.Ordinal)
                        || !DateTimeOffset.TryParse(FirstStringProperty(item, "expires"), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var expires)
                        || expires <= DateTimeOffset.UtcNow.AddMinutes(1))
                        continue;

                    session.TrialConversionPaymentUrl = new UriBuilder(uri) { Fragment = "/trials" }.Uri.AbsoluteUri;
                    session.TrialConversionPaymentUrlExpiresAt = expires;
                    return session.TrialConversionPaymentUrl;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Authentication is read-only; retrying cannot submit another charge.
            throw new TrialConversionRejectedException("Could not open the FastSpring account portal. Please try again.");
        }
        throw new TrialConversionRejectedException("Could not open the FastSpring account portal. Please try again.");
    }

    private async Task<FastSpringApiResponse<JsonDocument>> GetFastSpringSubscription(
        ZenmeterDemoSession session, CancellationToken cancellationToken)
    {
        FastSpringApiResponse<JsonDocument> response;
        try
        {
            response = await fastSpring.GetSubscription(session.SubscriptionRefId!, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // This is a read failure before the portal link is prepared, so it can be retried.
            throw new TrialConversionRejectedException("Could not read the FastSpring subscription. Please try again.");
        }
        if (!response.IsSuccessStatusCode || response.Payload is null)
        {
            response.Dispose();
            throw new TrialConversionRejectedException("Could not read the FastSpring subscription. Please try again.");
        }
        return response;
    }

    private static string? FirstStringProperty(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

}
