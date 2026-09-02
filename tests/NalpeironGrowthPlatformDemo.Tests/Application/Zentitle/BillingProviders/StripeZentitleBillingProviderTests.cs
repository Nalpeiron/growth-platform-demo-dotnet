using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
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
    public void Capabilities_WhenRead_SupportOnlyYearlyExternalCheckout()
    {
        // arrange
        var provider = Provider(new RecordingStripeHandler([]));

        // assert
        Assert.Equal(BillingSystem.Stripe, provider.BillingSystem);
        Assert.Equal([BillingPeriod.Yearly], provider.Capabilities.SupportedPaidPeriods);
        Assert.False(provider.Capabilities.SupportsPaidPeriod(BillingPeriod.Perpetual));
        Assert.False(provider.Capabilities.SupportsTrialCheckout);
        Assert.False(provider.Capabilities.SupportsUpgrade);
        Assert.True(provider.Capabilities.UsesExternalCheckout);
        Assert.Equal(ZentitlePriceSource.BillingProvider, provider.Capabilities.PriceSource);
        Assert.Equal(
            new BillingPriceRecurrence(BillingPriceInterval.Year, 1),
            provider.Capabilities.RequiredPriceRecurrence);
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

    [Fact]
    public async Task CreateCheckout_WithExistingStripeCustomer_ReusesCustomerAndSendsOrionMetadata()
    {
        // arrange
        var handler = new RecordingStripeHandler([
            new(HttpMethod.Get, "/v1/prices",
                """{"data":[{"id":"price_1","lookup_key":"sku-1","unit_amount":49900,"currency":"usd","type":"recurring","recurring":{"interval":"year","interval_count":1}}]}"""),
            new(HttpMethod.Get, "/v1/customers/search", """{"data":[{"id":"cus_existing"}]}"""),
            new(HttpMethod.Post, "/v1/customers/cus_existing", """{"id":"cus_existing"}"""),
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"url":"https://checkout.stripe.test/session"}""")
        ]);

        // act
        var result = await Provider(handler).CreateCheckout(PendingCheckout(), CancellationToken.None);

        // assert
        Assert.Equal(ZentitleCheckoutStatuses.Pending, result.Status);
        Assert.Equal("https://checkout.stripe.test/session", result.RedirectUrl);
        Assert.DoesNotContain(handler.Requests, request => request.Path == "/v1/customers" &&
                                                           request.Method == HttpMethod.Post);
        var customer = Assert.Single(handler.Requests, request => request.Path == "/v1/customers/cus_existing");
        Assert.Equal("Acme", customer.Form["name"]);
        Assert.Equal("account-ref-1", customer.Form["metadata[customer_ref]"]);
        var request = Assert.Single(handler.Requests, candidate => candidate.Path == "/v1/checkout/sessions");
        Assert.Equal("subscription", request.Form["mode"]);
        Assert.Equal("session-1", request.Form["client_reference_id"]);
        Assert.Equal("cus_existing", request.Form["customer"]);
        Assert.Equal("price_1", request.Form["line_items[0][price]"]);
        Assert.Equal("1", request.Form["line_items[0][quantity]"]);
        Assert.Equal("demo-order-1", request.Form["metadata[order_ref_id]"]);
        Assert.Equal("demo-order-1", request.Form["subscription_data[metadata][order_ref_id]"]);
        Assert.Equal("account-ref-1", request.Form["subscription_data[metadata][customer_ref]"]);
        Assert.Equal("zentitle_purchase", request.Form["subscription_data[metadata][billing_purpose]"]);
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
            new(HttpMethod.Post, "/v1/checkout/sessions", """{"url":"https://checkout.stripe.test/session"}""")
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
        Assert.Equal("sub_1", session.ProviderSubscriptionRefId);
    }

    [Theory]
    [InlineData("one_time", null)]
    [InlineData("recurring", "month")]
    public async Task CreateCheckout_WithNonYearlyStripePrice_ThrowsBeforeCreatingACustomer(
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
        var act = () => provider.CreateCheckout(PendingCheckout(), CancellationToken.None);

        // assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("yearly recurring Price", exception.Message);
        Assert.DoesNotContain(handler.Requests, request => request.Path.StartsWith("/v1/customers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindProvisionedGroup_WithCheckoutSessionRef_LooksUpByApplicationOrderRef()
    {
        // arrange
        var group = new Zt.EntitlementGroupModel { Id = "group-1" };
        var zentitle = new Mock<IZentitleManagementClient>(MockBehavior.Strict);
        zentitle
            .Setup(candidate => candidate.LookupGroup(
                "customer-1",
                "demo-order-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(group);
        var session = Session();
        session.ProviderOrderRefId = "cs_1";

        // act
        var result = await Provider(
            new RecordingStripeHandler([]),
            zentitle: zentitle.Object).FindProvisionedGroup(session, CancellationToken.None);

        // assert
        Assert.Same(group, result);
        zentitle.VerifyAll();
    }

    private static StripeZentitleBillingProvider Provider(
        RecordingStripeHandler handler,
        BillingOptions? billingOptions = null,
        IZentitleManagementClient? zentitle = null)
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
            "sku-1");

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
