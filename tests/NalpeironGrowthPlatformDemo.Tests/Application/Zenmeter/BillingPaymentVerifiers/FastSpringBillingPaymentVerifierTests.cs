using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingPaymentVerifiers;

public sealed class FastSpringBillingPaymentVerifierTests
{
    private static readonly BillingTopUpPayment Payment = new(
        "order-1",
        "operation-1",
        "_demo-z2-topup-1",
        "credits-50k-onetime",
        "session-1",
        "zm-sub-1");

    [Fact]
    public async Task VerifyTopUp_CompletedMatchingOrder_ReturnsCompleted()
    {
        // arrange
        var apiClient = CreateApiClient(HttpStatusCode.OK, CompletedOrder());
        var verifier = CreateVerifier(apiClient.Object);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Completed, result.Status);
        apiClient.Verify(
            client => client.GetOrder("order-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyTopUp_OrderNotCompleted_ReturnsPending()
    {
        // arrange
        var apiClient = CreateApiClient(
            HttpStatusCode.OK,
            CompletedOrder().Replace("\"completed\": true", "\"completed\": false"));
        var verifier = CreateVerifier(apiClient.Object);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Pending, result.Status);
    }

    [Fact]
    public async Task VerifyTopUp_CompletedOrderForDifferentOperation_ReturnsFailed()
    {
        // arrange
        var apiClient = CreateApiClient(
            HttpStatusCode.OK,
            CompletedOrder().Replace("operation-1", "operation-other"));
        var verifier = CreateVerifier(apiClient.Object);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Failed, result.Status);
        Assert.Contains("does not match", result.Error);
    }

    [Fact]
    public async Task VerifyTopUp_OrderTemporarilyUnavailable_ReturnsPending()
    {
        // arrange
        var apiClient = CreateApiClient(HttpStatusCode.BadRequest, "{}");
        var verifier = CreateVerifier(apiClient.Object);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Pending, result.Status);
    }

    [Fact]
    public async Task VerifyTopUp_WithSuccessfulOrder_DisposesTheResponsePayload()
    {
        // arrange
        using var document = JsonDocument.Parse(CompletedOrder());
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(client => client.GetOrder("order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse<JsonDocument>(
                HttpStatusCode.OK,
                CompletedOrder(),
                document));
        var verifier = CreateVerifier(apiClient.Object);

        // act
        var result = await verifier.VerifyTopUp(Payment, CancellationToken.None);

        // assert
        Assert.Equal(BillingPaymentVerificationStatus.Completed, result.Status);
        Assert.Throws<ObjectDisposedException>(() => document.RootElement.ValueKind);
    }

    private static FastSpringBillingPaymentVerifier CreateVerifier(IFastSpringBillingApiClient apiClient) =>
        new(apiClient, NullLogger<FastSpringBillingPaymentVerifier>.Instance);

    private static Mock<IFastSpringBillingApiClient> CreateApiClient(HttpStatusCode statusCode, string body)
    {
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(client => client.GetOrder("order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new FastSpringApiResponse<JsonDocument>(
                statusCode,
                body,
                IsSuccessStatusCode(statusCode)
                    ? JsonDocument.Parse(body)
                    : null));
        return apiClient;
    }

    private static bool IsSuccessStatusCode(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static string CompletedOrder() =>
        """
        {
          "orders": [
            {
              "id": "order-1",
              "completed": true,
              "items": [
                { "product": "credits-50k-onetime", "quantity": 1 }
              ],
              "tags": {
                "billing_purpose": "top_up",
                "top_up_operation_id": "operation-1",
                "top_up_sku": "credits-50k-onetime",
                "order_ref_id": "_demo-z2-topup-1",
                "demo_session_id": "session-1",
                "target_subscription_id": "zm-sub-1"
              }
            }
          ]
        }
        """;
}
