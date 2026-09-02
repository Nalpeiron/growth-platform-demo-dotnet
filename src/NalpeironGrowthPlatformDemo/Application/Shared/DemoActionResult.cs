namespace NalpeironGrowthPlatformDemo.Application.Shared;

public sealed record DemoActionResult(bool Succeeded, string? Code, string? Message)
{
    public static DemoActionResult Success() => new(true, null, null);

    public static DemoActionResult Failure(string code, string message) => new(false, code, message);
}
