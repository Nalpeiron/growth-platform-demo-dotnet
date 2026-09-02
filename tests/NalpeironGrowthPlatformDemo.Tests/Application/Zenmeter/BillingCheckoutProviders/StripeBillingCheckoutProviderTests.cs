using System.Net;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingCheckoutProviders;

public sealed class StripeBillingCheckoutProviderTests
{
    [Fact]
    public async Task CreateCheckout_WithInvalidZenmeterUrl_ThrowsBeforeCallingStripe()
    {
        // arrange
        var handler = new RecordingStripeHandler([]);
        var options = BillingCheckoutTestData.CreateBillingOptions();
        options.Stripe.ZenmeterSuccessUrl = "";
        var provider = CreateProvider(handler, options);

        // act
        var act = () => provider.CreateCheckout(
            BillingCheckoutTestData.CreateCheckout(),
            CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("Billing:Stripe:ZenmeterSuccessUrl", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateCheckout_WithExistingStripeCustomer_UsesCustomerOnCheckoutSession()
    {
        // arrange
        var handler = new RecordingStripeHandler([
            new(HttpMethod.Get, "/v1/prices",
                """{"data":[{"id":"price_1","lookup_key":"elevate-saas-launch-monthly","unit_amount":4900,"currency":"usd"}]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[{"id":"cus_existing"}]}"""),
            new(HttpMethod.Post, "/v1/customers/cus_existing", """{"id":"cus_existing"}"""),
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"url":"https://checkout.stripe.test/session"}""")
        ]);
        var provider = CreateProvider(handler);
        var checkout = BillingCheckoutTestData.CreateCheckout();

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        Assert.Equal(ZenmeterCheckoutStatuses.Pending, result.Status);
        Assert.Equal("https://checkout.stripe.test/session", result.RedirectUrl);
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post &&
                                                           request.Path == "/v1/customers");
        var customerSearch = Assert.Single(handler.Requests, request => request.Path == "/v1/customers/search");
        Assert.Contains("account-ref-1", Uri.UnescapeDataString(customerSearch.Query));

        var checkoutRequest = Assert.Single(handler.Requests, request => request.Path == "/v1/checkout/sessions");
        Assert.Equal("cus_existing", checkoutRequest.Form["customer"]);
        Assert.Equal("order-1", checkoutRequest.Form["subscription_data[metadata][order_ref_id]"]);
        Assert.Equal("account-ref-1", checkoutRequest.Form["subscription_data[metadata][customer_ref]"]);
        Assert.False(checkoutRequest.Form.ContainsKey("subscription_data[metadata][user_ref]"));
    }

    [Fact]
    public async Task CreateCheckout_WithMultiplePrices_AddsEachPriceAsCheckoutLineItem()
    {
        // arrange
        var handler = new RecordingStripeHandler([
            new(HttpMethod.Get, "/v1/prices",
                """{"data":[{"id":"price_base","lookup_key":"base-sku","unit_amount":4900,"currency":"usd"},{"id":"price_recurring_addon","lookup_key":"recurring-addon-sku","unit_amount":2900,"currency":"usd"},{"id":"price_one_time_addon","lookup_key":"one-time-addon-sku","unit_amount":1500,"currency":"usd"}]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[{"id":"cus_existing"}]}"""),
            new(HttpMethod.Post, "/v1/customers/cus_existing", """{"id":"cus_existing"}"""),
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"url":"https://checkout.stripe.test/session"}""")
        ]);
        var provider = CreateProvider(handler);
        var checkout = BillingCheckoutTestData.CreateCheckout([
            "base-sku",
            "recurring-addon-sku",
            "one-time-addon-sku"
        ]);

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        Assert.Equal("https://checkout.stripe.test/session", result.RedirectUrl);
        var checkoutRequest = Assert.Single(handler.Requests, request => request.Path == "/v1/checkout/sessions");
        Assert.Equal("price_base", checkoutRequest.Form["line_items[0][price]"]);
        Assert.Equal("price_recurring_addon", checkoutRequest.Form["line_items[1][price]"]);
        Assert.Equal("price_one_time_addon", checkoutRequest.Form["line_items[2][price]"]);
    }

    [Fact]
    public async Task CreateCheckout_WhenStripeCustomerDoesNotExist_CreatesCustomerBeforeCheckoutSession()
    {
        // arrange
        var handler = new RecordingStripeHandler([
            new(HttpMethod.Get, "/v1/prices",
                """{"data":[{"id":"price_1","lookup_key":"elevate-saas-launch-monthly","unit_amount":4900,"currency":"usd"}]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[]}"""),
            new(HttpMethod.Post, "/v1/customers", """{"id":"cus_new"}"""),
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"url":"https://checkout.stripe.test/session"}""")
        ]);
        var provider = CreateProvider(handler);
        var checkout = BillingCheckoutTestData.CreateCheckout();

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        Assert.Equal("https://checkout.stripe.test/session", result.RedirectUrl);

        var customerRequest = Assert.Single(handler.Requests, request => request.Path == "/v1/customers");
        Assert.Equal("Acme", customerRequest.Form["name"]);
        Assert.Equal("account-ref-1", customerRequest.Form["metadata[customer_ref]"]);
        Assert.Equal("Acme", customerRequest.Form["metadata[customer_name]"]);

        var checkoutRequest = Assert.Single(handler.Requests, request => request.Path == "/v1/checkout/sessions");
        Assert.Equal("cus_new", checkoutRequest.Form["customer"]);
        Assert.Equal("account-ref-1", checkoutRequest.Form["subscription_data[metadata][customer_ref]"]);
    }

    [Fact]
    public async Task CreateCheckout_WithLegacyCustomerIdMetadata_ReusesExistingStripeCustomer()
    {
        // arrange
        var handler = new RecordingStripeHandler([
            new(HttpMethod.Get, "/v1/prices",
                """{"data":[{"id":"price_1","lookup_key":"elevate-saas-launch-monthly","unit_amount":4900,"currency":"usd"}]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[{"id":"cus_legacy"}]}"""),
            new(HttpMethod.Post, "/v1/customers/cus_legacy", """{"id":"cus_legacy"}"""),
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"url":"https://checkout.stripe.test/session"}""")
        ]);
        var provider = CreateProvider(handler);

        // act
        var result = await provider.CreateCheckout(
            BillingCheckoutTestData.CreateCheckout(),
            CancellationToken.None);

        // assert
        Assert.Equal("https://checkout.stripe.test/session", result.RedirectUrl);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/v1/customers");
        var searches = handler.Requests.Where(request => request.Path == "/v1/customers/search").ToArray();
        Assert.Equal(2, searches.Length);
        Assert.Contains("account-ref-1", Uri.UnescapeDataString(searches[0].Query));
        Assert.Contains("customer-1", Uri.UnescapeDataString(searches[1].Query));
        var customerUpdate = Assert.Single(
            handler.Requests,
            request => request.Path == "/v1/customers/cus_legacy");
        Assert.Equal("account-ref-1", customerUpdate.Form["metadata[customer_ref]"]);

        var checkoutRequest = Assert.Single(handler.Requests, request => request.Path == "/v1/checkout/sessions");
        Assert.Equal("cus_legacy", checkoutRequest.Form["customer"]);
        Assert.Equal("account-ref-1", checkoutRequest.Form["subscription_data[metadata][customer_ref]"]);
    }

    [Fact]
    public async Task CreateCheckout_ForTopUp_UsesPaymentModeAndTargetSubscriptionMetadata()
    {
        // arrange
        var handler = new RecordingStripeHandler([
            new(HttpMethod.Get, "/v1/prices",
                """{"data":[{"id":"price_topup","lookup_key":"credits-50k-onetime","unit_amount":2900,"currency":"usd"}]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[{"id":"cus_existing"}]}"""),
            new(HttpMethod.Post, "/v1/customers/cus_existing", """{"id":"cus_existing"}"""),
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"url":"https://checkout.stripe.test/topup"}""")
        ]);
        var provider = CreateProvider(handler);
        var checkout = BillingCheckoutTestData.CreateCheckout(["credits-50k-onetime"]) with
        {
            Purpose = BillingCheckoutPurpose.TopUp,
            OperationId = "topup-1",
            TargetSubscriptionId = "subscription-1",
            TargetSubscriptionRefId = "provider-subscription-1"
        };

        // act
        var result = await provider.CreateCheckout(checkout, CancellationToken.None);

        // assert
        Assert.Equal("https://checkout.stripe.test/topup", result.RedirectUrl);
        var request = Assert.Single(handler.Requests, candidate => candidate.Path == "/v1/checkout/sessions");
        Assert.Equal("payment", request.Form["mode"]);
        Assert.Equal("top_up", request.Form["metadata[billing_purpose]"]);
        Assert.Equal("topup-1", request.Form["metadata[top_up_operation_id]"]);
        Assert.Equal("credits-50k-onetime", request.Form["metadata[top_up_sku]"]);
        Assert.Equal("session-1", request.Form["metadata[demo_session_id]"]);
        Assert.Equal("subscription-1", request.Form["metadata[target_subscription_id]"]);
        Assert.False(request.Form.ContainsKey("subscription_data[metadata][order_ref_id]"));
        Assert.Contains("topUpOperationId=topup-1", request.Form["success_url"]);
        Assert.Contains("providerOrderRefId={CHECKOUT_SESSION_ID}", request.Form["success_url"]);
        Assert.EndsWith("/elevate/saas/workspace", request.Form["cancel_url"]);
    }

    private static StripeBillingCheckoutProvider CreateProvider(
        RecordingStripeHandler handler,
        BillingOptions? billingOptions = null)
    {
        var httpClientFactory = new TestHttpClientFactory(new HttpClient(handler));
        var options = Options.Create(billingOptions ?? BillingCheckoutTestData.CreateBillingOptions());
        var clientFactory = new StripeBillingClientFactory(httpClientFactory, options);
        return new StripeBillingCheckoutProvider(
            options,
            new StripeBillingPriceProvider(clientFactory),
            clientFactory,
            new StripeBillingCustomerService(clientFactory));
    }

    private sealed class RecordingStripeHandler(IReadOnlyList<StripeResponse> responses) : HttpMessageHandler
    {
        private int _nextResponse;

        public List<RecordedStripeRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var form = request.Content is null
                ? new Dictionary<string, string>()
                : BillingCheckoutTestData.ParseForm(await request.Content.ReadAsStringAsync(cancellationToken));
            var recorded = new RecordedStripeRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                form);
            Requests.Add(recorded);

            var response = responses[_nextResponse++];
            Assert.Equal(response.Method, request.Method);
            Assert.Equal(response.Path, request.RequestUri.AbsolutePath);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.Body)
            };
        }
    }

    private sealed record StripeResponse(HttpMethod Method, string Path, string Body);

    private sealed record RecordedStripeRequest(
        HttpMethod Method,
        string Path,
        string Query,
        IReadOnlyDictionary<string, string> Form);
}
