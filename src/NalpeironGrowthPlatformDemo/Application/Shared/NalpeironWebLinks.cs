namespace NalpeironGrowthPlatformDemo.Application.Shared;

internal static class NalpeironWebLinks
{
    public static string? Build(
        string? baseUrl,
        string productSegment,
        string resourceSegment,
        string? id,
        string? fragment = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var normalizedBase = NormalizeBaseUrl(baseUrl);
        var url = $"{normalizedBase}/{productSegment.Trim('/')}/{resourceSegment.Trim('/')}/{id}";
        return string.IsNullOrEmpty(fragment) ? url : $"{url}#{fragment}";
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');
        foreach (var productSegment in new[] { "/zentitle", "/zenmeter" })
        {
            if (normalized.EndsWith(productSegment, StringComparison.OrdinalIgnoreCase))
            {
                return normalized[..^productSegment.Length].TrimEnd('/');
            }
        }

        return normalized;
    }
}
