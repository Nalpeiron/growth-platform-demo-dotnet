using NalpeironGrowthPlatformDemo.Application.Shared;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter.BillingTopUpPurchaseProviders;

public static class BillingTopUpResults
{
    public static ZenmeterTopUpResult Success(string? redirectUrl = null, string? operationId = null) =>
        new(DemoActionResult.Success(), redirectUrl, operationId);

    public static ZenmeterTopUpResult ConfirmationRequired(ZenmeterTopUpConfirmation confirmation) =>
        new(DemoActionResult.Success(), Confirmation: confirmation);

    public static ZenmeterTopUpResult Failure(string code, string message) =>
        new(DemoActionResult.Failure(code, message));
}
