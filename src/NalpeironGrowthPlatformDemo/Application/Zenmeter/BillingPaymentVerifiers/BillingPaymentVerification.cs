namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingPaymentVerifiers;

public sealed record BillingPaymentVerification(
    BillingPaymentVerificationStatus Status,
    string? Error = null)
{
    public static BillingPaymentVerification Pending(string? error = null) =>
        new(BillingPaymentVerificationStatus.Pending, error);

    public static BillingPaymentVerification Completed() =>
        new(BillingPaymentVerificationStatus.Completed);

    public static BillingPaymentVerification Failed(string error) =>
        new(BillingPaymentVerificationStatus.Failed, error);
}
