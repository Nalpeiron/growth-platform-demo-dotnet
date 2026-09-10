using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Stripe;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Shared.Billing.Stripe;

public sealed class StripeCheckoutResolverTests
{
    [Fact]
    public async Task Resolve_WithPaidPerpetualCheckout_ReturnsPaymentIntentReference()
    {
        // arrange
        var resolver = Resolver(PaymentCheckout());

        // act
        var result = await resolver.Resolve("cs_1", "session-1", "_demo-z2-customer", "zentitle_purchase",
            StripeCheckoutMode.Payment, CancellationToken.None);

        // assert
        Assert.Equal(new StripeCheckoutReferences("pi_1", null), result);
    }

    [Theory]
    [InlineData("status", "open")]
    [InlineData("payment_status", "unpaid")]
    [InlineData("payment_status", "no_payment_required")]
    [InlineData("payment_intent", null)]
    public async Task Resolve_WithPendingPerpetualPayment_ReturnsNull(string field, string? value)
    {
        // arrange
        var checkout = PaymentCheckout();
        checkout[field] = value;
        var resolver = Resolver(checkout);

        // act
        var result = await resolver.Resolve("cs_1", "session-1", "_demo-z2-customer", "zentitle_purchase",
            StripeCheckoutMode.Payment, CancellationToken.None);

        // assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("id", "cs_other")]
    [InlineData("object", "payment_intent")]
    [InlineData("mode", "subscription")]
    [InlineData("client_reference_id", "other-session")]
    [InlineData("customer_ref", "other-customer")]
    [InlineData("demo_session_id", "other-session")]
    [InlineData("billing_purpose", "subscription_purchase")]
    [InlineData("billing_purpose", null)]
    [InlineData("status", "expired")]
    [InlineData("invoice", "in_1")]
    [InlineData("subscription", "sub_1")]
    public async Task Resolve_WithExpiredOrMismatchedPerpetualCheckout_Throws(string field, string? value)
    {
        // arrange
        var checkout = PaymentCheckout();
        var target = field is "customer_ref" or "demo_session_id" or "billing_purpose"
            ? checkout["metadata"]!
            : checkout;
        target[field] = value;
        var resolver = Resolver(checkout);

        // act
        var act = () => resolver.Resolve("cs_1", "session-1", "_demo-z2-customer", "zentitle_purchase",
            StripeCheckoutMode.Payment, CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Theory]
    [InlineData("subscription_purchase", "paid")]
    [InlineData("zentitle_purchase", "paid")]
    [InlineData("subscription_purchase", "no_payment_required")]
    public async Task Resolve_WithCompletedCheckout_ReturnsInvoiceAndSubscriptionReferences(string purpose, string paymentStatus)
    {
        // arrange
        var checkout = Checkout();
        checkout["metadata"]!["billing_purpose"] = purpose;
        checkout["payment_status"] = paymentStatus;
        var resolver = Resolver(checkout);

        // act
        var result = await resolver.Resolve("cs_1", "session-1", "_demo-z2-customer", purpose, StripeCheckoutMode.Subscription, CancellationToken.None);

        // assert
        Assert.Equal(new StripeCheckoutReferences("in_1", "sub_1"), result);
    }

    [Theory]
    [InlineData("status", "open")]
    [InlineData("payment_status", "unpaid")]
    [InlineData("invoice", null)]
    [InlineData("subscription", null)]
    public async Task Resolve_WhenPaymentOrReferencesArePending_ReturnsNull(string field, string? value)
    {
        // arrange
        var checkout = Checkout();
        checkout[field] = value;
        var resolver = Resolver(checkout);

        // act
        var result = await resolver.Resolve("cs_1", "session-1", "_demo-z2-customer", "subscription_purchase", StripeCheckoutMode.Subscription, CancellationToken.None);

        // assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("id", "cs_other")]
    [InlineData("object", "subscription")]
    [InlineData("mode", "payment")]
    [InlineData("client_reference_id", "other-session")]
    [InlineData("customer_ref", "other-customer")]
    [InlineData("demo_session_id", "other-session")]
    [InlineData("billing_purpose", "top_up")]
    [InlineData("billing_purpose", null)]
    [InlineData("status", "expired")]
    public async Task Resolve_WithExpiredOrMismatchedCheckout_Throws(string field, string? value)
    {
        // arrange
        var checkout = Checkout();
        var target = field is "customer_ref" or "demo_session_id" or "billing_purpose"
            ? checkout["metadata"]!
            : checkout;
        target[field] = value;
        var resolver = Resolver(checkout);

        // act
        var act = () => resolver.Resolve("cs_1", "session-1", "_demo-z2-customer", "subscription_purchase", StripeCheckoutMode.Subscription, CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Resolve_WithTransientStripeFailure_ReturnsNull(HttpStatusCode status)
    {
        // arrange
        var resolver = Resolver(JsonNode.Parse("""{"error":{"type":"api_error","message":"Retry later"}}""")!, status);

        // act
        var result = await resolver.Resolve("cs_1", "session-1", "_demo-z2-customer", "subscription_purchase", StripeCheckoutMode.Subscription, CancellationToken.None);

        // assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Resolve_WithUnknownCheckout_ThrowsStripeException()
    {
        // arrange
        var resolver = Resolver(JsonNode.Parse("""{"error":{"type":"invalid_request_error","message":"No such session"}}""")!, HttpStatusCode.NotFound);

        // act
        var act = () => resolver.Resolve("cs_1", "session-1", "_demo-z2-customer", "subscription_purchase", StripeCheckoutMode.Subscription, CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<StripeException>(act);
    }

    private static JsonNode PaymentCheckout()
    {
        var checkout = Checkout();
        checkout["mode"] = "payment";
        checkout["invoice"] = null;
        checkout["subscription"] = null;
        checkout["payment_intent"] = "pi_1";
        checkout["metadata"]!["billing_purpose"] = "zentitle_purchase";
        return checkout;
    }

    private static JsonNode Checkout() => JsonNode.Parse("""
        {"id":"cs_1","object":"checkout.session","mode":"subscription","status":"complete",
         "payment_status":"paid","client_reference_id":"session-1","invoice":"in_1","subscription":"sub_1",
         "metadata":{"customer_ref":"_demo-z2-customer","demo_session_id":"session-1","billing_purpose":"subscription_purchase"}}
        """)!;

    private static StripeCheckoutResolver Resolver(JsonNode response, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StripeBillingClientFactory(
                new TestHttpClientFactory(new HttpClient(new CheckoutHandler(response.ToJsonString(), status))),
                Options.Create(new BillingOptions
                {
                    Stripe = new StripeBillingOptions { ApiUrl = "https://stripe.test", SecretKey = "sk_test" }
                })),
            NullLogger<StripeCheckoutResolver>.Instance);

    private sealed class CheckoutHandler(string response, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1/checkout/sessions/cs_1", request.RequestUri!.AbsolutePath);
            var result = new HttpResponseMessage(status) { Content = new StringContent(response) };
            result.Headers.Add("Stripe-Should-Retry", "false");
            return Task.FromResult(result);
        }
    }
}
