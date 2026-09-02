namespace NalpeironGrowthPlatformDemo.Application.Shared;

/// <summary>
/// Guards against open redirects when a candidate redirect target is taken from user input
/// (query string, form field, etc.) and is expected to stay within this app.
/// </summary>
public static class LocalRedirectGuard
{
    /// <summary>
    /// Returns true only for paths that are unambiguously local to this app: a single leading
    /// "/" followed by anything other than another "/" or "\". Browsers treat "//host" and
    /// "/\host" as protocol-relative URLs, so a naive `StartsWith("/")` check is not enough to
    /// prevent redirecting off-site.
    /// </summary>
    public static bool IsSafeLocalPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith('/') &&
        path.Length > 1 &&
        path[1] is not ('/' or '\\');
}