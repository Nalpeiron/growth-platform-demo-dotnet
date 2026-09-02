namespace NalpeironGrowthPlatformDemo.Components;

/// <summary>
/// Browser <c>localStorage</c> keys that hold only the demo session reference id.
/// The session payload itself stays server-side in the session store; the browser keeps just
/// the id and hands it back over the Interactive Server circuit (see <see cref="BrowserStorage"/>).
/// </summary>
public static class SessionStorageKey
{
    public const string Zentitle = "zentitle-demo-app-session";
    public const string Zenmeter = "zenmeter-demo-app-session";
}
