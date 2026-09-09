using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing;
using NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;
using NalpeironGrowthPlatformDemo.Application.Zentitle;
using NalpeironGrowthPlatformDemo.Application.Zentitle.BillingProviders;
using NalpeironGrowthPlatformDemo.Configuration;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle;
using Zt = NalpeironGrowthPlatformDemo.Nalpeiron.Zentitle.Generated;
using NalpeironGrowthPlatformDemo.Tests.TestHelpers;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Application.Zentitle.BillingProviders;

public sealed class StripeZentitleBillingProviderTests
{
    [Fact]
    public void Capabilities_WhenRead_SupportYearlyAndPerpetualExternalCheckout()
    {
        // arrange
        var provider = Provider(new RecordingStripeHandler([]));

        // assert
        Assert.Equal(BillingSystem.Stripe, provider.BillingSystem);
        Assert.Equal([BillingPeriod.Yearly, BillingPeriod.Perpetual], provider.Capabilities.SupportedPaidPeriods);
        Assert.True(provider.Capabilities.SupportsPaidPeriod(BillingPeriod.Perpetual));
        Assert.False(provider.Capabilities.SupportsTrialCheckout);
        Assert.False(provider.Capabilities.SupportsUpgrade);
        Assert.True(provider.Capabilities.UsesExternalCheckout);
        Assert.Equal(ZentitlePriceSource.BillingProvider, provider.Capabilities.PriceSource);
        Assert.True(provider.Capabilities.RequiresMatchingPriceRecurrence);
    }

    [Theory]
    [InlineData("", "https://api.stripe.test", "https://demo.test/success", "https://demo.test/cancel", "SecretKey")]
    [InlineData("sk_test", "not-a-url", "https://demo.test/success", "https://demo.test/cancel", "ApiUrl")]
    [InlineData("sk_test", "https://api.stripe.test", "", "https://demo.test/cancel", "ZentitleSuccessUrl")]
    [InlineData("sk_test", "https://api.stripe.test", "https://demo.test/success", "relative", "ZentitleCancelUrl")]
    public void ConfigurationUnavailableReason_WithMissingOrInvalidSetting_NamesTheSetting(
        string secretKey,
        string apiUrl,
        string successUrl,
        string cancelUrl,
        string expectedSetting)
    {
        // arrange
        var options = BillingOptions();
        options.Stripe.SecretKey = secretKey;
        options.Stripe.ApiUrl = apiUrl;
        options.Stripe.ZentitleSuccessUrl = successUrl;
        options.Stripe.ZentitleCancelUrl = cancelUrl;

        // act
        var reason = Provider(new RecordingStripeHandler([]), options).ConfigurationUnavailableReason();

        // assert
        Assert.Contains(expectedSetting, reason);
    }

    [Theory]
    [InlineData(BillingPeriod.Yearly)]
    [InlineData(BillingPeriod.Perpetual)]
    public async Task CreateCheckout_WithExistingStripeCustomer_ReusesCustomerAndSendsOrionMetadata(BillingPeriod period)
    {
        // arrange
        var isPerpetual = period == BillingPeriod.Perpetual;
        var priceResponse = isPerpetual
            ? """{"data":[{"id":"price_1","lookup_key":"sku-1","unit_amount":49900,"currency":"usd","type":"one_time"}]}"""
            : """{"data":[{"id":"price_1","lookup_key":"sku-1","unit_amount":49900,"currency":"usd","type":"recurring","recurring":{"interval":"year","interval_count":1}}]}""";
        var handler = new RecordingStripeHandler([
            new(HttpMethod.Get, "/v1/prices", priceResponse),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[{"id":"cus_existing"}]}"""),
            new(HttpMethod.Post, "/v1/customers/cus_existing", """{"id":"cus_existing"}"""),
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"id":"cs_1","url":"https://checkout.stripe.test/session"}""")
        ]);

        // act
        var result = await Provider(handler).CreateCheckout(PendingCheckout() with { Period = period }, CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Pending, result.Status);
        Assert.Equal("https://checkout.stripe.test/session", result.RedirectUrl);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/v1/customers" &&
                                                           request.Method == HttpMethod.Post);
        var customer = Assert.Single(handler.Requests, request => request.Path == "/v1/customers/cus_existing");
        Assert.Equal("Acme", customer.Form["name"]);
        Assert.Equal("account-ref-1", customer.Form["metadata[customer_ref]"]);
        var request = Assert.Single(handler.Requests, candidate => candidate.Path == "/v1/checkout/sessions");
        Assert.Equal(isPerpetual ? "payment" : "subscription", request.Form["mode"]);
        Assert.Equal("session-1", request.Form["client_reference_id"]);
        Assert.Equal("cus_existing", request.Form["customer"]);
        Assert.Equal("price_1", request.Form["line_items[0][price]"]);
        Assert.Equal("1", request.Form["line_items[0][quantity]"]);
        Assert.False(request.Form.ContainsKey("metadata[order_ref_id]"));
        Assert.False(request.Form.ContainsKey("subscription_data[metadata][order_ref_id]"));
        Assert.Equal("cs_1", result.ProviderCheckoutSessionId);
        Assert.Equal("session-1", request.Form["metadata[demo_session_id]"]);
        Assert.Equal("account-ref-1", request.Form["metadata[customer_ref]"]);
        Assert.Equal("zentitle_purchase", request.Form["metadata[billing_purpose]"]);
        var metadataTarget = isPerpetual ? "payment_intent_data" : "subscription_data";
        Assert.Equal("session-1", request.Form[$"{metadataTarget}[metadata][demo_session_id]"]);
        Assert.Equal("account-ref-1", request.Form[$"{metadataTarget}[metadata][customer_ref]"]);
        Assert.Equal("zentitle_purchase", request.Form[$"{metadataTarget}[metadata][billing_purpose]"]);
        Assert.DoesNotContain(request.Form.Keys, key => key.StartsWith(isPerpetual ? "subscription_data" : "payment_intent_data"));
        Assert.DoesNotContain(request.Form.Keys, key => key.StartsWith("invoice_creation"));
        Assert.Contains("sessionId=session-1", request.Form["success_url"]);
        Assert.Contains("providerOrderRefId={CHECKOUT_SESSION_ID}", request.Form["success_url"]);
        Assert.Contains("offeringId=offering-1", request.Form["cancel_url"]);
    }

    [Fact]
    public async Task CreateCheckout_WhenStripeCustomerDoesNotExist_CreatesCustomerWithNameAndCustomerRef()
    {
        // arrange
        var handler = new RecordingStripeHandler([
            new(HttpMethod.Get, "/v1/prices",
                """{"data":[{"id":"price_1","lookup_key":"sku-1","unit_amount":49900,"currency":"usd","type":"recurring","recurring":{"interval":"year","interval_count":1}}]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[]}"""),
            new(HttpMethod.Post, "/v1/customers", """{"id":"cus_new"}"""),
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"id":"cs_1","url":"https://checkout.stripe.test/session"}""")
        ]);

        // act
        await Provider(handler).CreateCheckout(PendingCheckout(), CancellationToken.None);

        // assert
        var customer = Assert.Single(handler.Requests, request => request.Path == "/v1/customers" &&
                                                          request.Method == HttpMethod.Post);
        Assert.Equal("Acme", customer.Form["name"]);
        Assert.Equal("account-ref-1", customer.Form["metadata[customer_ref]"]);
        Assert.Equal("Acme", customer.Form["metadata[customer_name]"]);
        var checkout = Assert.Single(handler.Requests, request => request.Path == "/v1/checkout/sessions");
        Assert.Equal("cus_new", checkout.Form["customer"]);
    }

    [Fact]
    public void ApplyReturn_WithRepeatedThenConflictingReferences_IsIdempotentAndRejectsTheConflict()
    {
        // arrange
        var provider = Provider(new RecordingStripeHandler([]));
        var session = Session();

        // act
        var first = provider.ApplyReturn(session, new ZentitleProviderReturnData("cs_1", "sub_1"));
        var repeated = provider.ApplyReturn(session, new ZentitleProviderReturnData("cs_1", "sub_1"));
        var conflict = provider.ApplyReturn(session, new ZentitleProviderReturnData("cs_other", "sub_1"));

        // assert
        Assert.Null(first.Error);
        Assert.Null(repeated.Error);
        Assert.Contains("different order reference", conflict.Error);
        Assert.Equal("cs_1", session.ProviderOrderRefId);
        Assert.Null(session.ProviderSubscriptionRefId);
    }

    [Theory]
    [InlineData(BillingPeriod.Yearly, "one_time", null)]
    [InlineData(BillingPeriod.Yearly, "recurring", "month")]
    [InlineData(BillingPeriod.Perpetual, "recurring", "year")]
    [InlineData(BillingPeriod.Perpetual, "recurring", "month")]
    public async Task CreateCheckout_WithMismatchedStripePrice_ThrowsBeforeCreatingACustomer(
        BillingPeriod period,
        string priceType,
        string? interval)
    {
        // arrange
        var priceResponse = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new
                {
                    id = "price_1",
                    lookup_key = "sku-1",
                    unit_amount = 49900,
                    currency = "usd",
                    type = priceType,
                    recurring = interval is null ? null : new { interval, interval_count = 1 }
                }
            }
        });
        var handler = new RecordingStripeHandler([
            new(
                HttpMethod.Get,
                "/v1/prices",
                priceResponse)
        ]);

        var provider = Provider(handler);

        // act
        var act = () => provider.CreateCheckout(PendingCheckout() with { Period = period }, CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains(period == BillingPeriod.Perpetual ? "one-time Price" : "yearly recurring Price", exception.Message);
        Assert.DoesNotContain(handler.Requests, request => request.Path.StartsWith("/v1/customers", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(BillingPeriod.Yearly, "in_1")]
    [InlineData(BillingPeriod.Perpetual, "pi_1")]
    public async Task FindProvisionedGroup_WithDelayedProvisioning_ReusesVerifiedStripeReference(BillingPeriod period, string orderRefId)
    {
        // arrange
        var group = new Zt.EntitlementGroupModel { Id = "group-1" };
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .SetupSequence(candidate => candidate.LookupGroup(
                "customer-1",
                orderRefId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Zt.EntitlementGroupModel?)null)
            .ReturnsAsync(group);
        var session = Session();
        session.Period = period;
        session.ProviderOrderRefId = "cs_1";

        var checkoutResponse = period == BillingPeriod.Perpetual
            ? """{"id":"cs_1","object":"checkout.session","mode":"payment","status":"complete","payment_status":"paid","client_reference_id":"session-1","payment_intent":"pi_1","metadata":{"customer_ref":"account-ref-1","demo_session_id":"session-1","billing_purpose":"zentitle_purchase"}}"""
            : """{"id":"cs_1","object":"checkout.session","mode":"subscription","status":"complete","payment_status":"paid","client_reference_id":"session-1","invoice":"in_1","subscription":"sub_1","metadata":{"customer_ref":"account-ref-1","demo_session_id":"session-1","billing_purpose":"zentitle_purchase"}}""";

        var provider = Provider(
            new RecordingStripeHandler([
                new(HttpMethod.Get, "/v1/checkout/sessions/cs_1", checkoutResponse)
            ]),
            zentitle: zentitle.Object);

        // act
        var pending = await provider.FindProvisionedGroup(session, CancellationToken.None);
        var result = await provider.FindProvisionedGroup(session, CancellationToken.None);

        // assert
        Assert.Null(pending);
        Assert.Same(group, result);
        Assert.Equal(orderRefId, session.OrderRefId);
        Assert.Equal(period == BillingPeriod.Perpetual ? null : "sub_1", session.ProviderSubscriptionRefId);
        Assert.True(session.IsProviderCheckoutVerified);
        zentitle.VerifyAll();
    }

    [Fact]
    public async Task FindProvisionedGroup_WithPendingCheckout_DoesNotLookUpTheGroup()
    {
        // arrange
        var resolver = new Mock<IStripeCheckoutResolver>();
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        var session = Session();
        session.ProviderOrderRefId = "cs_1";
        var provider = Provider(new RecordingStripeHandler([]), zentitle: zentitle.Object, checkoutResolver: resolver.Object);

        // act
        var result = await provider.FindProvisionedGroup(session, CancellationToken.None);

        // assert
        Assert.Null(result);
        Assert.Equal("demo-order-1", session.OrderRefId);
        zentitle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FindProvisionedGroup_WithMismatchedCheckout_ThrowsBeforeLookup()
    {
        // arrange
        var resolver = new Mock<IStripeCheckoutResolver>();
        resolver.Setup(x => x.Resolve("cs_1", "session-1", "account-ref-1", "zentitle_purchase", It.IsAny<CancellationToken>(), StripeCheckoutMode.Subscription))
            .ThrowsAsync(new InvalidOperationException("Mismatched checkout"));
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        var session = Session();
        session.ProviderOrderRefId = "cs_1";
        var provider = Provider(new RecordingStripeHandler([]), zentitle: zentitle.Object, checkoutResolver: resolver.Object);

        // act
        var act = () => provider.FindProvisionedGroup(session, CancellationToken.None);

        // assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        zentitle.VerifyNoOtherCalls();
    }

    private static StripeZentitleBillingProvider Provider(
        RecordingStripeHandler handler,
        BillingOptions? billingOptions = null,
        IZentitleManagementClient? zentitle = null,
        IStripeCheckoutResolver? checkoutResolver = null)
    {
        var options = Options.Create(billingOptions ?? BillingOptions());
        var httpClientFactory = new TestHttpClientFactory(new HttpClient(handler));
        var clientFactory = new StripeBillingClientFactory(httpClientFactory, options);
        var priceProvider = new StripeBillingPriceProvider(clientFactory);
        var priceResolver = new BillingPriceResolver([priceProvider], options);
        return new StripeZentitleBillingProvider(
            options,
            priceResolver,
            clientFactory,
            new StripeBillingCustomerService(clientFactory),
            checkoutResolver ?? new StripeCheckoutResolver(clientFactory, NullLogger<StripeCheckoutResolver>.Instance),
            zentitle ?? Mock.Of<IZentitleManagementClient>());
    }

    private static BillingOptions BillingOptions() =>
        new()
        {
            EnabledBillingSystems = [BillingSystem.Stripe],
            Stripe = new StripeBillingOptions
            {
                ApiUrl = "https://api.stripe.test",
                SecretKey = "sk_test",
                ZentitleSuccessUrl = "https://demo.test/elevate/billing/return",
                ZentitleCancelUrl = "https://demo.test/elevate/stripe/checkout"
            }
        };

    private static ZentitlePendingCheckout PendingCheckout() =>
        new(
            "session-1",
            "Acme",
            "customer-1",
            "account-ref-1",
            "demo-order-1",
            "offering-1",
            "sku-1", BillingPeriod.Yearly);

    private static ElevateSession Session() =>
        new()
        {
            SessionId = "session-1",
            CustomerName = "Acme",
            ProductId = "product-1",
            EditionId = "edition-1",
            Period = BillingPeriod.Yearly,
            Sku = "sku-1",
            BillingSystem = BillingSystem.Stripe,
            CustomerId = "customer-1",
            CustomerAccountRefId = "account-ref-1",
            OrderRefId = "demo-order-1",
            CheckoutStatus = ZentitleCheckoutStatuses.Pending
        };

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
                : BillingCheckoutTestData.ParseForm(
                    await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(new RecordedStripeRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                form));

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
