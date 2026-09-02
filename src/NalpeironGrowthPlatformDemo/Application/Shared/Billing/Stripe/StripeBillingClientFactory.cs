using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Configuration;
using Stripe;

namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;

public sealed class StripeBillingClientFactory(
    IHttpClientFactory httpClientFactory,
    IOptions<BillingOptions> billingOptions)
{
    public StripeClient Create()
    {
        var stripe = billingOptions.Value.Stripe;
        return new StripeClient(
            stripe.SecretKey,
            httpClient: new SystemNetHttpClient(httpClientFactory.CreateClient()),
            apiBase: stripe.ApiUrl);
    }
}
