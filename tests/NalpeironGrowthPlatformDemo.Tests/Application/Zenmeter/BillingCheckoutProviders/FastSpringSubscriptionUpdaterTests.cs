using System.Net;
using System.Text.Json;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingCheckoutProviders;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter.BillingCheckoutProviders;

public sealed class FastSpringSubscriptionUpdaterTests
{
    [Fact]
    public async Task AddRecurringAddon_WhenSubscriptionHasNoSuchAddon_UpdatesItWithQuantityOne()
    {
        // arrange
        object? updatePayload = null;
        var apiClient = CreateApiClient(SubscriptionResponse());
        apiClient
            .Setup(client => client.UpdateSubscription(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((payload, _) => updatePayload = payload)
            .ReturnsAsync(new FastSpringApiResponse(HttpStatusCode.OK, UpdateSuccessResponse()));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        await updater.AddRecurringAddon(" subscription-1 ", " credits-500-monthly ", CancellationToken.None);

        // assert
        apiClient.Verify(
            client => client.GetSubscription("subscription-1", It.IsAny<CancellationToken>()),
            Times.Once);
        apiClient.Verify(
            client => client.UpdateSubscription(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotNull(updatePayload);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(updatePayload));
        var update = body.RootElement.GetProperty("subscriptions")[0];
        Assert.Equal("subscription-1", update.GetProperty("subscription").GetString());
        Assert.True(update.GetProperty("prorate").GetBoolean());
        Assert.False(update.TryGetProperty("preview", out _));

        var addon = update.GetProperty("addons")[0];
        Assert.Equal("credits-500-monthly", addon.GetProperty("product").GetString());
        Assert.Equal(1, addon.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AddRecurringAddon_WhenSubscriptionAlreadyHasTheAddon_RaisesItsQuantity()
    {
        // arrange
        object? updatePayload = null;
        var apiClient = CreateApiClient(SubscriptionResponse(("CREDITS-500-MONTHLY", 2)));
        apiClient
            .Setup(client => client.UpdateSubscription(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((payload, _) => updatePayload = payload)
            .ReturnsAsync(new FastSpringApiResponse(HttpStatusCode.OK, UpdateSuccessResponse()));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        await updater.AddRecurringAddon("subscription-1", "credits-500-monthly", CancellationToken.None);

        // assert
        Assert.NotNull(updatePayload);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(updatePayload));
        var addon = body.RootElement.GetProperty("subscriptions")[0].GetProperty("addons")[0];
        Assert.Equal(3, addon.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AddRecurringAddon_WhenSubscriptionLookupFails_ThrowsWithoutUpdatingTheSubscription()
    {
        // arrange
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(client => client.GetSubscription(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse<JsonDocument>(
                HttpStatusCode.NotFound,
                "subscription not found",
                Payload: null));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        var act = () => updater.AddRecurringAddon("subscription-1", "credits-500-monthly", CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<FastSpringApiRequestException>(act);
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("subscription not found", error.ResponseBody);
        apiClient.Verify(
            client => client.UpdateSubscription(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EstimateRecurringAddon_WhenSubscriptionLookupFails_ThrowsWithoutEstimatingTheUpdate()
    {
        // arrange
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(client => client.GetSubscription(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse<JsonDocument>(
                HttpStatusCode.Unauthorized,
                "invalid credentials",
                Payload: null));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        var act = () => updater.EstimateRecurringAddon(
            "subscription-1",
            "credits-500-monthly",
            CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<FastSpringApiRequestException>(act);
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal("invalid credentials", error.ResponseBody);
        apiClient.Verify(
            client => client.EstimateSubscriptionUpdate(It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EstimateRecurringAddon_WhenSubscriptionAlreadyHasTheAddon_PreviewsTheRaisedQuantity()
    {
        // arrange
        object? updatePayload = null;
        var apiClient = CreateApiClient(SubscriptionResponse(("credits-500-monthly", 1)));
        apiClient
            .Setup(client => client.EstimateSubscriptionUpdate(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((payload, _) => updatePayload = payload)
            .ReturnsAsync(new FastSpringApiResponse(HttpStatusCode.OK, EstimateResponse()));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        var estimate = await updater.EstimateRecurringAddon(
            " subscription-1 ",
            " credits-500-monthly ",
            CancellationToken.None);

        // assert
        Assert.Equal("$12.34", estimate.AmountDueDisplay);
        Assert.Equal("$149.00", estimate.NextChargeAmountDisplay);
        Assert.Equal("2026-08-28", estimate.NextChargeDateDisplay);

        Assert.NotNull(updatePayload);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(updatePayload));
        var update = body.RootElement;
        Assert.Equal("subscription-1", update.GetProperty("subscription").GetString());
        Assert.True(update.GetProperty("prorate").GetBoolean());
        Assert.False(update.TryGetProperty("preview", out _));

        var addon = update.GetProperty("addons")[0];
        Assert.Equal("credits-500-monthly", addon.GetProperty("product").GetString());
        Assert.Equal(2, addon.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AddRecurringAddon_WhenFastSpringRejectsTheUpdate_Throws()
    {
        // arrange
        var apiClient = CreateApiClient(SubscriptionResponse());
        apiClient
            .Setup(client => client.UpdateSubscription(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse(HttpStatusCode.BadRequest, "proration unavailable"));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        var act = () => updater.AddRecurringAddon("subscription-1", "credits-500-monthly", CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<FastSpringApiRequestException>(act);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("proration unavailable", error.ResponseBody);
    }

    [Fact]
    public async Task AddRecurringAddon_WhenFastSpringReturnsOperationError_Throws()
    {
        // arrange
        const string responseBody =
            """
            {
              "subscriptions": [
                {
                  "subscription": "subscription-1",
                  "action": "subscription.update",
                  "result": "error",
                  "error": {
                    "subscription": "Subscription update is not allowed."
                  }
                }
              ]
            }
            """;
        var apiClient = CreateApiClient(SubscriptionResponse());
        apiClient
            .Setup(client => client.UpdateSubscription(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse(HttpStatusCode.OK, responseBody));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        var act = () => updater.AddRecurringAddon(
            "subscription-1",
            "credits-500-monthly",
            CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<FastSpringApiRequestException>(act);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Equal(responseBody, error.ResponseBody);
    }

    [Fact]
    public async Task AddRecurringAddon_WhenUpdateResponseOmitsTheSubscription_Throws()
    {
        // arrange
        var apiClient = CreateApiClient(SubscriptionResponse());
        apiClient
            .Setup(client => client.UpdateSubscription(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse(HttpStatusCode.OK, """{"subscriptions": []}"""));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        var act = () => updater.AddRecurringAddon("subscription-1", "credits-500-monthly", CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<JsonException>(act);
    }

    [Fact]
    public async Task EstimateRecurringAddon_WhenFastSpringRejectsPreview_Throws()
    {
        // arrange
        var apiClient = CreateApiClient(SubscriptionResponse());
        apiClient
            .Setup(client => client.EstimateSubscriptionUpdate(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse(
                HttpStatusCode.BadRequest,
                "Proration is not allowed."));
        var updater = new FastSpringSubscriptionUpdater(apiClient.Object);

        // act
        var act = () => updater.EstimateRecurringAddon(
            "subscription-1",
            "credits-500-monthly",
            CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<FastSpringApiRequestException>(act);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("Proration is not allowed.", error.ResponseBody);
    }

    private static Mock<IFastSpringBillingApiClient> CreateApiClient(string subscriptionBody)
    {
        var apiClient = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(client => client.GetSubscription(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new FastSpringApiResponse<JsonDocument>(
                HttpStatusCode.OK,
                subscriptionBody,
                JsonDocument.Parse(subscriptionBody)));
        return apiClient;
    }

    private static string SubscriptionResponse(params (string Product, int Quantity)[] addons)
    {
        var entries = addons.Select(addon =>
            $$"""{"product": "{{addon.Product}}", "quantity": {{addon.Quantity}}}""");
        return $$"""{"subscription": "subscription-1", "addons": [{{string.Join(",", entries)}}]}""";
    }

    private static string EstimateResponse() =>
        """
        {
          "amountDue": {
            "totalAmountDueDisplay": "$12.34",
            "nextChargeAmountDisplay": "$149.00",
            "nextChargeDateDisplayISO8601": "2026-08-28"
          }
        }
        """;

    private static string UpdateSuccessResponse() =>
        """
        {
          "subscriptions": [
            {
              "subscription": "subscription-1",
              "action": "subscription.update",
              "result": "success"
            }
          ]
        }
        """;
}
