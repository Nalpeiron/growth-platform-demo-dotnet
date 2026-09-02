namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;

public sealed record BillingTopUpPayment(
    string ProviderOrderRefId,
    string OperationId,
    string OrderRefId,
    string Sku,
    string DemoSessionId,
    string TargetSubscriptionId);
