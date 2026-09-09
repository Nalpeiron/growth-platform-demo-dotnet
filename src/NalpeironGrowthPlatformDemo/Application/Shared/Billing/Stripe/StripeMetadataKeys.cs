namespace NalpeironGrowthPlatformDemo.Application.Shared.Billing.Stripe;

// Wire names shared by the demo checkout, payment verification and Orion integration.
public static class StripeMetadataKeys
{
    public const string CustomerRef = "customer_ref";
    public const string CustomerName = "customer_name";
    public const string DemoSessionId = "demo_session_id";
    public const string BillingPurpose = "billing_purpose";
    public const string OrderRefId = "order_ref_id";
    public const string ExternalUserId = "external_user_id";
    public const string UserEmail = "user_email";
    public const string TopUpOperationId = "top_up_operation_id";
    public const string TopUpSku = "top_up_sku";
    public const string TargetSubscriptionId = "target_subscription_id";
    public const string TargetSubscriptionRefId = "target_subscription_ref_id";
}
