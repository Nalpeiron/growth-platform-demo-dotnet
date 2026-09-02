using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingPaymentVerifiers;

public sealed class StripeBillingPaymentVerifierTests
{
    private static readonly BillingTopUpPayment Payment = new(
        "cs_topup_1",
        "operation-1",
        "_demo-z2-topup-1",
        "credits-50k-onetime",
        "session-1",
        "zm-sub-1");

    [Fact]
    public async Task VerifyTopUp_PaidMatchingCheckoutSession_ReturnsCompleted()
    {
        // arrange
        var handler = new StripeVerificationHandler([
            new(HttpStatusCode.OK, PaidSession()),
            new(HttpStatusCode.OK, LineItems("credits-50k-onetime"))
        ]);
        var verifier = CreateVerifier(handler);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Completed, result.Status);
        Assert.Equal(
            [
                "/v1/checkout/sessions/cs_topup_1",
                "/v1/checkout/sessions/cs_topup_1/line_items"
            ],
            handler.Requests.Select(request => request.Path));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Authorization?.Scheme);
            Assert.Equal("sk_test_demo", request.Authorization?.Parameter);
        });
    }

    [Fact]
    public async Task VerifyTopUp_UnpaidCheckoutSession_ReturnsPendingWithoutLoadingLineItems()
    {
        // arrange
        var handler = new StripeVerificationHandler([
            new(HttpStatusCode.OK, PaidSession().Replace("\"paid\"", "\"unpaid\""))
        ]);
        var verifier = CreateVerifier(handler);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Pending, result.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task VerifyTopUp_PaidSessionForDifferentOperation_ReturnsFailed()
    {
        // arrange
        var handler = new StripeVerificationHandler([
            new(HttpStatusCode.OK, PaidSession().Replace("operation-1", "operation-other"))
        ]);
        var verifier = CreateVerifier(handler);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Failed, result.Status);
        Assert.Contains("does not match", result.Error);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task VerifyTopUp_PaidSessionWithDifferentProduct_ReturnsFailed()
    {
        // arrange
        var handler = new StripeVerificationHandler([
            new(HttpStatusCode.OK, PaidSession()),
            new(HttpStatusCode.OK, LineItems("different-product"))
        ]);
        var verifier = CreateVerifier(handler);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Failed, result.Status);
        Assert.Contains("does not contain", result.Error);
    }

    [Fact]
    public async Task VerifyTopUp_TransientStripeFailure_ReturnsPending()
    {
        // arrange
        var verifier = CreateVerifier(new StripeVerificationHandler([
            new(HttpStatusCode.ServiceUnavailable, "{}")
        ]));

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Pending, result.Status);
    }

    private static StripeBillingPaymentVerifier CreateVerifier(StripeVerificationHandler handler) =>
        new(
            new TestHttpClientFactory(new HttpClient(handler)),
            Options.Create(new BillingOptions
            {
                Stripe = new StripeBillingOptions
                {
                    ApiUrl = "https://api.stripe.test",
                    SecretKey = "sk_test_demo"
                }
            }),
            NullLogger<StripeBillingPaymentVerifier>.Instance);

    private static string PaidSession() =>
        """
        {
          "id": "cs_topup_1",
          "object": "checkout.session",
          "client_reference_id": "session-1",
          "mode": "payment",
          "status": "complete",
          "payment_status": "paid",
          "metadata": {
            "billing_purpose": "top_up",
            "top_up_operation_id": "operation-1",
            "top_up_sku": "credits-50k-onetime",
            "order_ref_id": "_demo-z2-topup-1",
            "demo_session_id": "session-1",
            "target_subscription_id": "zm-sub-1"
          }
        }
        """;

    private static string LineItems(string lookupKey) =>
        $$"""
        {
          "object": "list",
          "data": [
            {
              "quantity": 1,
              "price": {
                "id": "price_topup",
                "lookup_key": "{{lookupKey}}"
              }
            }
          ]
        }
        """;

    private sealed class StripeVerificationHandler(IReadOnlyList<StripeResponse> responses) : HttpMessageHandler
    {
        private int _nextResponse;
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                request.Headers.Authorization));
            var response = responses[Math.Min(_nextResponse++, responses.Count - 1)];
            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body)
            });
        }
    }

    private sealed record StripeResponse(HttpStatusCode StatusCode, string Body);

    private sealed record RecordedRequest(
        string Path,
        string Query,
        AuthenticationHeaderValue? Authorization);
}
