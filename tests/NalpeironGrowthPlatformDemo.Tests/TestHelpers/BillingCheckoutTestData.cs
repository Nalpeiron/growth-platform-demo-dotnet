using NalpeironGrowthPlatformDemo.Application.Zenmeter;
using NalpeironGrowthPlatformDemo.Configuration;

namespace NalpeironGrowthPlatformDemo.Tests.TestHelpers;

internal static class BillingCheckoutTestData
{
    public static BillingOptions CreateBillingOptions() =>
        new()
        {
            Stripe = new StripeBillingOptions
            {
                ApiUrl = "https://api.stripe.test",
                SecretKey = "sk_test",
                ZenmeterSuccessUrl = "https://demo.test/success",
                ZenmeterCancelUrl = "https://demo.test/cancel",
                ZentitleSuccessUrl = "https://demo.test/zentitle/success",
                ZentitleCancelUrl = "https://demo.test/zentitle/cancel"
            },
            FastSpring = new FastSpringBillingOptions
            {
                ApiUrl = "https://api.fastspring.test",
                ZenmeterStorefrontUrl = "demo-store.test.onfastspring.com/popup-zenmeter",
                ZentitleStorefrontUrl = "demo-store.test.onfastspring.com/popup-zentitle",
                ApiUsername = "user",
                ApiPassword = "password"
            }
        };

    public static ZenmeterPendingCheckout CreateCheckout(
        IReadOnlyList<string>? skus = null) =>
        new(
            "session-1",
            "Acme",
            "customer-1",
            "account-ref-1",
            new ZenmeterUserDetails("alex.morgan", "Alex", "Morgan", "alex.morgan@acme.test"),
            "order-1",
            skus ?? ["elevate-saas-launch-monthly"]);

    public static Dictionary<string, string> ParseForm(string body) =>
        ParseEncodedPairs(body);

    public static Dictionary<string, string> ParseQuery(string query) =>
        ParseEncodedPairs(query.TrimStart('?'));

    private static Dictionary<string, string> ParseEncodedPairs(string value) =>
        value.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(DecodePartKey, DecodePartValue);

    private static string DecodePartKey(string[] pair) =>
        Uri.UnescapeDataString(pair[0].Replace("+", " ", StringComparison.Ordinal));

    private static string DecodePartValue(string[] pair) =>
        pair.Length == 2
            ? Uri.UnescapeDataString(pair[1].Replace("+", " ", StringComparison.Ordinal))
            : "";
}
