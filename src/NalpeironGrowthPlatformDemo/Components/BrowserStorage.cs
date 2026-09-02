using Microsoft.JSInterop;

namespace NalpeironGrowthPlatformDemo.Components;

/// <summary>
/// Thin wrapper over the browser's <c>localStorage</c> for the one-time demo session id.
/// Backed by JS interop, so calls are only valid once the component is interactive
/// (i.e. from <c>OnAfterRenderAsync</c> or event handlers, never during prerender).
/// </summary>
public sealed class BrowserStorage(IJSRuntime js)
{
    public async ValueTask<string?> GetAsync(string key) =>
        await js.InvokeAsync<string?>("localStorage.getItem", key);

    public async ValueTask SetAsync(string key, string value) =>
        await js.InvokeVoidAsync("localStorage.setItem", key, value);

    public async ValueTask RemoveAsync(string key) =>
        await js.InvokeVoidAsync("localStorage.removeItem", key);
}
