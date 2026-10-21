using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zenmeter;

public sealed class ZenmeterTrialBillingTests
{
    private const string CardlessTrial = """{"id":"sub_1","customer":"cus_1","status":"trialing","trial_end":2000000000}""";
    private const string PendingTrial = """{"id":"sub_1","customer":"cus_1","status":"trialing","trial_end":2000000000,"pending_update":{"expires_at":1900000000},"latest_invoice":{"id":"in_conversion","status":"open","billing_reason":"subscription_update","hosted_invoice_url":"https://invoice.stripe.test/pay"}}""";

    [Fact]
    public async Task StartConversion_StripeSavedCard_EnablesSavingReplacementCardBeforeConditionalPayment()
    {
        // arrange
        const string trial = """{"id":"sub_1","status":"trialing","default_payment_method":"pm_saved"}""";
        var handler = new RecordingHandler(trial, trial, PendingTrial);

        // act
        var url = await Create(handler).StartConversion(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        Assert.Equal("https://invoice.stripe.test/pay", url);
        var updates = handler.Requests.Where(request => request.Method == HttpMethod.Post).ToArray();
        Assert.Equal(2, updates.Length);
        Assert.Equal("on_subscription", updates[0].Form["payment_settings[save_default_payment_method]"]);
        Assert.False(updates[0].Form.ContainsKey("trial_end"));
        Assert.Equal("pending_if_incomplete", updates[1].Form["payment_behavior"]);
        Assert.DoesNotContain(updates[1].Form.Keys, key => key.StartsWith("payment_settings"));
    }

    [Fact]
    public async Task GetPaymentUrl_StripeTrialPaidWhileCollectingCard_DoesNotChargeAgain()
    {
        // arrange
        var handler = new RecordingHandler("""{"id":"sub_1","customer":"cus_1","status":"active","latest_invoice":{"id":"in_paid","status":"paid"}}""");

        // act
        var url = await Create(handler).GetPaymentUrl(SetupSession(), CancellationToken.None);

        // assert
        Assert.Null(url);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task StartConversion_StripeWithoutCard_CollectsCardWithoutEndingTrial()
    {
        // arrange
        var handler = new RecordingHandler(CardlessTrial,
            """{"id":"cs_setup","url":"https://checkout.stripe.test/setup"}""");
        var session = Session(BillingSystem.Stripe);

        // act
        var url = await Create(handler).StartConversion(session, CancellationToken.None);

        // assert
        Assert.Equal("https://checkout.stripe.test/setup", url);
        Assert.Equal("cs_setup", session.TrialConversionSetupSessionId);
        var request = Assert.Single(handler.Requests, x => x.Method == HttpMethod.Post);
        Assert.Equal("/v1/checkout/sessions", request.Path);
        Assert.Equal("setup", request.Form["mode"]);
        Assert.Equal("cus_1", request.Form["customer"]);
        Assert.Equal("session", request.Form["client_reference_id"]);
        Assert.Equal("sub_1", request.Form["metadata[subscription_ref]"]);
        Assert.Equal("trial_conversion", request.Form["metadata[billing_purpose]"]);
        Assert.Contains("trialConversion=true", request.Form["success_url"]);
        Assert.EndsWith("/elevate/saas/workspace", request.Form["cancel_url"]);
        Assert.Equal("trial-setup-session-attempt", request.IdempotencyKey);
        Assert.DoesNotContain(request.Form.Keys, key => key.StartsWith("subscription_data") || key == "trial_end");
    }

    [Fact]
    public async Task GetPaymentUrl_StripeSetupAbandoned_ReusesCheckoutWithoutChangingTrial()
    {
        // arrange
        var handler = new RecordingHandler(CardlessTrial, SetupCheckout("open"));
        var session = SetupSession();

        // act
        var url = await Create(handler).GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Equal("https://checkout.stripe.test/setup", url);
        Assert.True(session.TrialConversionPending);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task GetPaymentUrl_StripeSetupCompleted_UsesVerifiedCardThenRequestsConditionalConversion()
    {
        // arrange
        var handler = new RecordingHandler(CardlessTrial, SetupCheckout("complete"), CardlessTrial, CardlessTrial, PendingTrial);
        var session = SetupSession();

        // act
        var url = await Create(handler).GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Equal("https://invoice.stripe.test/pay", url);
        var updates = handler.Requests.Where(request => request.Method == HttpMethod.Post).ToArray();
        Assert.Equal(2, updates.Length);
        Assert.All(updates, request => Assert.Equal("/v1/subscriptions/sub_1", request.Path));
        Assert.Equal("pm_verified", updates[0].Form["default_payment_method"]);
        Assert.Equal("on_subscription", updates[0].Form["payment_settings[save_default_payment_method]"]);
        Assert.False(updates[0].Form.ContainsKey("trial_end"));
        Assert.Equal("pending_if_incomplete", updates[1].Form["payment_behavior"]);
        Assert.Equal("now", updates[1].Form["trial_end"]);
        Assert.Equal("trial-conversion-session-attempt", updates[1].IdempotencyKey);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("mode")]
    [InlineData("reference")]
    [InlineData("subscription")]
    [InlineData("purpose")]
    [InlineData("setup_customer")]
    [InlineData("setup_status")]
    public async Task GetPaymentUrl_StripeSetupMismatch_RejectsWithoutCharging(string mismatch)
    {
        // arrange
        var handler = new RecordingHandler(CardlessTrial, SetupCheckout("complete", mismatch));
        var billing = Create(handler);

        // act
        var act = () => billing.GetPaymentUrl(SetupSession(), CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<TrialConversionRejectedException>(act);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task GetPaymentUrl_StripeSetupExpired_AllowsFreshAttemptWithoutCharging()
    {
        // arrange
        var handler = new RecordingHandler(CardlessTrial, SetupCheckout("expired"));
        var session = SetupSession();

        // act
        var url = await Create(handler).GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Null(url);
        Assert.False(session.TrialConversionPending);
        Assert.Null(session.TrialConversionSetupSessionId);
        Assert.Null(session.TrialConversionRequestId);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task GetPaymentUrl_StripeConversionInvoiceVoided_AllowsFreshAttemptWithoutCharging()
    {
        // arrange
        var handler = new RecordingHandler("""{"id":"sub_1","status":"trialing","latest_invoice":{"id":"in_conversion","status":"void","billing_reason":"subscription_update"}}""");
        var session = SetupSession();

        // act
        var url = await Create(handler).GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Null(url);
        Assert.False(session.TrialConversionPending);
        Assert.Null(session.TrialConversionRequestId);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task StartConversion_StripePendingUpdate_ReusesInvoiceWithoutAnotherCharge()
    {
        // arrange
        var handler = new RecordingHandler(PendingTrial);

        // act
        var url = await Create(handler).StartConversion(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        Assert.Equal("https://invoice.stripe.test/pay", url);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task GetPaymentUrl_StripeInterruptedConversion_ReplaysOriginalIdempotencyKey()
    {
        // arrange
        var handler = new RecordingHandler(
            """{"id":"sub_1","status":"trialing","default_payment_method":"pm_saved","payment_settings":{"save_default_payment_method":"on_subscription"}}""", PendingTrial);
        var session = Session(BillingSystem.Stripe);
        session.TrialConversionPending = true;

        // act
        var url = await Create(handler).GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Equal("https://invoice.stripe.test/pay", url);
        Assert.Equal("trial-conversion-session-attempt",
            Assert.Single(handler.Requests, request => request.Method == HttpMethod.Post).IdempotencyKey);
    }

    private static ZenmeterDemoSession SetupSession()
    {
        var session = Session(BillingSystem.Stripe);
        session.TrialConversionPending = true;
        session.TrialConversionSetupSessionId = "cs_setup";
        return session;
    }

    private static string SetupCheckout(string status, string? mismatch = null) => JsonSerializer.Serialize(new
    {
        id = "cs_setup", status, url = "https://checkout.stripe.test/setup",
        mode = mismatch == "mode" ? "payment" : "setup",
        customer = mismatch == "customer" ? "cus_other" : "cus_1",
        client_reference_id = mismatch == "reference" ? "another-session" : "session",
        metadata = new
        {
            demo_session_id = "session", subscription_ref = mismatch == "subscription" ? "sub_other" : "sub_1",
            billing_purpose = mismatch == "purpose" ? "another-purpose" : "trial_conversion"
        },
        setup_intent = new
        {
            id = "seti_1", status = mismatch == "setup_status" ? "requires_action" : "succeeded",
            customer = mismatch == "setup_customer" ? "cus_other" : "cus_1", payment_method = "pm_verified"
        }
    });

    [Fact]
    public async Task StartConversion_StripeTrial_RequestsPaymentConditionalConversionWithIdempotencyKey()
    {
        // arrange
        var handler = new RecordingHandler(
            """{"id":"sub_1","object":"subscription","status":"trialing","default_payment_method":"pm_saved","payment_settings":{"save_default_payment_method":"on_subscription"}}""",
            """{"id":"sub_1","object":"subscription","status":"trialing","pending_update":{"expires_at":2000000000},"latest_invoice":{"id":"in_1","status":"open","hosted_invoice_url":"https://invoice.stripe.test/pay"}}""");
        var billing = Create(handler);

        // act
        var paymentUrl = await billing.StartConversion(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        var update = Assert.Single(handler.Requests, x => x.Method == HttpMethod.Post);
        Assert.Equal("/v1/subscriptions/sub_1", update.Path);
        Assert.Equal("now", update.Form["trial_end"]);
        Assert.Equal("none", update.Form["proration_behavior"]);
        Assert.Equal("pending_if_incomplete", update.Form["payment_behavior"]);
        Assert.DoesNotContain(update.Form.Keys, key => key.StartsWith("trial_settings") || key.StartsWith("payment_settings"));
        Assert.Equal("https://invoice.stripe.test/pay", paymentUrl);
        Assert.Equal("trial-conversion-session-attempt", update.IdempotencyKey);
        Assert.DoesNotContain(update.Form.Keys, key => key.StartsWith("items"));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("past_due")]
    public async Task StartConversion_StripeAlreadyBilled_DoesNotBillAgain(string state)
    {
        // arrange
        var handler = new RecordingHandler(JsonSerializer.Serialize(new { id = "sub_1", status = state }));
        var billing = Create(handler);

        // act
        await billing.StartConversion(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Theory]
    [InlineData("canceled", false)]
    [InlineData("trialing", true)]
    public async Task StartConversion_StripeCancelled_RejectsWithoutUpdating(string state, bool cancelAtPeriodEnd)
    {
        // arrange
        var handler = new RecordingHandler(JsonSerializer.Serialize(new { id = "sub_1", status = state, cancel_at_period_end = cancelAtPeriodEnd }));
        var billing = Create(handler);

        // act
        var act = () => billing.StartConversion(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<TrialConversionRejectedException>(act);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("open", "https://invoice.stripe.test/pay", true)]
    [InlineData("paid", "https://invoice.stripe.test/pay", false)]
    [InlineData("open", "javascript:alert(1)", false)]
    public async Task GetPaymentUrl_StripeInvoice_OnlyReturnsUnpaidHttpsInvoice(string status, string url, bool expected)
    {
        // arrange
        var handler = new RecordingHandler(JsonSerializer.Serialize(new
        {
            id = "sub_1", latest_invoice = new { id = "in_1", status, hosted_invoice_url = url }
        }));
        var billing = Create(handler);

        // act
        var result = await billing.GetPaymentUrl(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        Assert.Equal(expected ? url : null, result);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task StartConversion_StripeDraftInvoice_FinalizesSameInvoiceWithoutAutomaticCollection()
    {
        // arrange
        var handler = new RecordingHandler(
            """{"id":"sub_1","status":"trialing","default_payment_method":"pm_saved","payment_settings":{"save_default_payment_method":"on_subscription"}}""",
            """{"id":"sub_1","status":"active","latest_invoice":{"id":"in_conversion","status":"draft","billing_reason":"subscription_cycle"}}""",
            """{"id":"in_conversion","status":"open","hosted_invoice_url":"https://invoice.stripe.test/pay"}""");
        var billing = Create(handler);

        // act
        var url = await billing.StartConversion(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        Assert.Equal("https://invoice.stripe.test/pay", url);
        var finalize = Assert.Single(handler.Requests, x => x.Path.EndsWith("/finalize"));
        Assert.Equal("/v1/invoices/in_conversion/finalize", finalize.Path);
        Assert.Equal("false", finalize.Form["auto_advance"]);
        Assert.Equal("trial-invoice-session-in_conversion", finalize.IdempotencyKey);
        Assert.DoesNotContain(handler.Requests, x => x.Path is "/v1/checkout/sessions" or "/v1/subscriptions" || x.Path.EndsWith("/pay"));
    }

    [Fact]
    public async Task GetPaymentUrl_StripeDraftAfterInterruptedFinalization_ResumesSameInvoice()
    {
        // arrange
        var handler = new RecordingHandler(
            """{"id":"sub_1","status":"active","latest_invoice":{"id":"in_conversion","status":"draft","billing_reason":"subscription_cycle"}}""",
            """{"id":"in_conversion","status":"open","hosted_invoice_url":"https://invoice.stripe.test/pay"}""");
        var billing = Create(handler);

        // act
        var url = await billing.GetPaymentUrl(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        Assert.Equal("https://invoice.stripe.test/pay", url);
        Assert.Equal("/v1/invoices/in_conversion/finalize", Assert.Single(handler.Requests, x => x.Method == HttpMethod.Post).Path);
    }

    [Theory]
    [InlineData("trialing", "subscription_create")]
    [InlineData("active", "subscription_create")]
    [InlineData("trialing", "subscription_cycle")]
    public async Task GetPaymentUrl_StripeInitialTrialInvoice_DoesNotFinalizeOrCharge(string state, string reason)
    {
        // arrange
        var handler = new RecordingHandler(JsonSerializer.Serialize(new
        {
            id = "sub_1", status = state,
            latest_invoice = new { id = "in_trial", status = "draft", billing_reason = reason }
        }));
        var billing = Create(handler);

        // act
        var url = await billing.GetPaymentUrl(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        Assert.Null(url);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task StartConversion_StripeExistingUnpaidInvoice_ReturnsItWithoutRestartingBilling()
    {
        // arrange
        var handler = new RecordingHandler(
            """{"id":"sub_1","status":"past_due","latest_invoice":{"id":"in_conversion","status":"open","hosted_invoice_url":"https://invoice.stripe.test/pay"}}""");
        var billing = Create(handler);

        // act
        var url = await billing.StartConversion(Session(BillingSystem.Stripe), CancellationToken.None);

        // assert
        Assert.Equal("https://invoice.stripe.test/pay", url);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task StartConversion_FastSpringSubscriptionReadFails_ReportsReadFailureWithoutUpdating(HttpStatusCode status)
    {
        // arrange
        var api = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        api.Setup(x => x.GetSubscription("sub_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FastSpringApiResponse<JsonDocument>(status, "{}", null));
        var billing = Create(new RecordingHandler(), api.Object);

        // act
        var act = () => billing.StartConversion(Session(BillingSystem.FastSpring), CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<TrialConversionRejectedException>(act);
        Assert.Equal("Could not read the FastSpring subscription. Please try again.", error.Message);
        api.Verify(x => x.GetSubscription("sub_1", It.IsAny<CancellationToken>()), Times.Once);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StartConversion_FastSpringTrial_ReturnsOfficialAccountPortalWithoutMutatingSubscription()
    {
        // arrange
        var api = FastSpring("trial");
        ConfigurePortal(api);
        var billing = Create(new RecordingHandler(), api.Object);
        var session = Session(BillingSystem.FastSpring);

        // act
        var checkout = await billing.StartConversion(session, CancellationToken.None);
        var resumed = await billing.GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Equal("https://store.test/account/token/auth#/trials", checkout);
        Assert.Equal(checkout, resumed);
        Assert.Equal(checkout, session.TrialConversionPaymentUrl);
        api.Verify(x => x.GetSubscription("sub_1", It.IsAny<CancellationToken>()), Times.Exactly(2));
        api.Verify(x => x.GetAccountManagementUrl("account-1", It.IsAny<CancellationToken>()), Times.Once);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPaymentUrl_FastSpringCachedSubscriptionsLink_OpensTrialsWithoutRefreshingAuthentication()
    {
        // arrange
        var api = FastSpring("trial");
        var billing = Create(new RecordingHandler(), api.Object);
        var session = Session(BillingSystem.FastSpring);
        session.TrialConversionPaymentUrl = "https://store.test/account/token/auth#/subscriptions";
        session.TrialConversionPaymentUrlExpiresAt = DateTimeOffset.UtcNow.AddHours(1);

        // act
        var checkout = await billing.GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Equal("https://store.test/account/token/auth#/trials", checkout);
        Assert.Equal(checkout, session.TrialConversionPaymentUrl);
        api.Verify(x => x.GetSubscription("sub_1", It.IsAny<CancellationToken>()), Times.Once);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPaymentUrl_FastSpringExpiredPortalLink_RefreshesAuthentication()
    {
        // arrange
        var api = FastSpring("trial");
        ConfigurePortal(api);
        var billing = Create(new RecordingHandler(), api.Object);
        var session = Session(BillingSystem.FastSpring);
        session.TrialConversionPaymentUrl = "https://store.test/account/expired#/trials";
        session.TrialConversionPaymentUrlExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        // act
        var checkout = await billing.GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Equal("https://store.test/account/token/auth#/trials", checkout);
        Assert.True(session.TrialConversionPaymentUrlExpiresAt > DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("overdue")]
    public async Task StartConversion_FastSpringAlreadyBilled_DoesNotOpenPortal(string state)
    {
        // arrange
        var api = FastSpring(state);
        var billing = Create(new RecordingHandler(), api.Object);

        // act
        var checkout = await billing.StartConversion(Session(BillingSystem.FastSpring), CancellationToken.None);

        // assert
        Assert.Null(checkout);
        api.Verify(x => x.GetSubscription("sub_1", It.IsAny<CancellationToken>()), Times.Once);
        api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("other-account", "success", "https://store.test/account/token/auth", 24)]
    [InlineData("account-1", "error", "https://store.test/account/token/auth", 24)]
    [InlineData("account-1", "success", "http://store.test/account/token/auth", 24)]
    [InlineData("account-1", "success", "https://store.test/session/unexpected", 24)]
    [InlineData("account-1", "success", "https://user@store.test/account/token/auth", 24)]
    [InlineData("account-1", "success", "https://store.test/account/token/auth", -1)]
    public async Task StartConversion_FastSpringInvalidAuthentication_RejectsWithoutChangingSubscription(string account, string result, string url, int hours)
    {
        // arrange
        var api = FastSpring("trial");
        var body = JsonSerializer.Serialize(new { accounts = new[] { new { account, result, url, expires = DateTimeOffset.UtcNow.AddHours(hours) } } });
        api.Setup(x => x.GetAccountManagementUrl("account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new FastSpringApiResponse<JsonDocument>(HttpStatusCode.OK, body, JsonDocument.Parse(body)));
        var billing = Create(new RecordingHandler(), api.Object);

        // act
        var act = () => billing.StartConversion(Session(BillingSystem.FastSpring), CancellationToken.None);

        // assert
        var error = await Assert.ThrowsAsync<TrialConversionRejectedException>(act);
        Assert.Contains("Could not open the FastSpring account portal", error.Message);
        api.Verify(x => x.GetSubscription("sub_1", It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(x => x.GetAccountManagementUrl("account-1", It.IsAny<CancellationToken>()), Times.Once);
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StartConversion_FastSpringAuthenticationUnavailable_DoesNotCacheFailedAttempt()
    {
        // arrange
        var api = FastSpring("trial");
        ConfigurePortal(api);
        api.Setup(x => x.GetAccountManagementUrl("account-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Authentication unavailable"));
        var session = Session(BillingSystem.FastSpring);
        var billing = Create(new RecordingHandler(), api.Object);

        // act
        var act = () => billing.StartConversion(session, CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<TrialConversionRejectedException>(act);
        Assert.Null(session.TrialConversionPaymentUrl);
        Assert.Null(session.TrialConversionPaymentUrlExpiresAt);
    }

    [Fact]
    public async Task GetPaymentUrl_FastSpringPaidWithCachedPortal_WaitsForProvisioningWithoutReopeningPortal()
    {
        // arrange
        var api = FastSpring("active");
        var session = Session(BillingSystem.FastSpring);
        session.TrialConversionPaymentUrl = "https://store.test/account/token/auth#/trials";
        session.TrialConversionPaymentUrlExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var billing = Create(new RecordingHandler(), api.Object);

        // act
        var url = await billing.GetPaymentUrl(session, CancellationToken.None);

        // assert
        Assert.Null(url);
        api.Verify(x => x.GetSubscription("sub_1", It.IsAny<CancellationToken>()), Times.Once);
        api.VerifyNoOtherCalls();
    }

    private static void ConfigurePortal(Mock<IFastSpringBillingApiClient> api)
    {
        var body = JsonSerializer.Serialize(new { accounts = new[] { new
        {
            account = "account-1", result = "success", url = "https://store.test/account/token/auth",
            expires = DateTimeOffset.UtcNow.AddHours(24)
        } } });
        api.Setup(x => x.GetAccountManagementUrl("account-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new FastSpringApiResponse<JsonDocument>(HttpStatusCode.OK, body, JsonDocument.Parse(body)));
    }

    private static Mock<IFastSpringBillingApiClient> FastSpring(string state)
    {
        var mock = new Mock<IFastSpringBillingApiClient>(MockBehavior.Strict);
        mock.Setup(x => x.GetSubscription("sub_1", It.IsAny<CancellationToken>())).ReturnsAsync(() =>
        {
            var body = JsonSerializer.Serialize(new { subscription = "sub_1", account = "account-1", state, priceDisplay = "$99.00", subtotalDisplay = "$0.00" });
            return new FastSpringApiResponse<JsonDocument>(HttpStatusCode.OK, body, JsonDocument.Parse(body));
        });
        return mock;
    }

    private static ZenmeterTrialBilling Create(RecordingHandler handler, IFastSpringBillingApiClient? fastSpring = null) =>
        new(new StripeTrialBilling(new StripeBillingClientFactory(new TestHttpClientFactory(new HttpClient(handler)),
            Options.Create(BillingCheckoutTestData.CreateBillingOptions())),
            Options.Create(BillingCheckoutTestData.CreateBillingOptions())),
            fastSpring ?? Mock.Of<IFastSpringBillingApiClient>());

    private static ZenmeterDemoSession Session(BillingSystem billingSystem,
        ZenmeterOfferingPeriod period = ZenmeterOfferingPeriod.Monthly) => new()
    {
        SessionId = "session", CustomerName = "Acme", TierKey = "tier", PlanSku = "plan",
        Period = period, SubscriptionId = "subscription",
        SubscriptionRefId = "sub_1", BillingSystem = billingSystem, TrialConversionRequestId = "attempt"
    };

    private sealed class RecordingHandler(params string[] responses) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = responses[Requests.Count];
            Requests.Add(new Request(request.Method, request.RequestUri!.AbsolutePath,
                request.Content is null ? new Dictionary<string, string>() : BillingCheckoutTestData.ParseForm(await request.Content.ReadAsStringAsync(cancellationToken)),
                request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed record Request(HttpMethod Method, string Path, IReadOnlyDictionary<string, string> Form, string? IdempotencyKey);
}
