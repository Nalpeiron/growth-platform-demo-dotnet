using System.Net;
using System.Text.Json;
using NalpeironGrowthPlatformDemo.Application.Shared;
using NalpeironGrowthPlatformDemo.Application.Zenmeter.Billing.FastSpring;
using NalpeironGrowthPlatformDemo.Nalpeiron.Generic;
using NalpeironGrowthPlatformDemo.Nalpeiron.Zenmeter.Generated;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class ZenmeterDemoErrors
{
    public static DemoActionResult ToActionError(
        Exception ex,
        string fallbackCode,
        string fallbackMessage) =>
        ex switch
        {
            ManagementApiException { ApiStatusCode: HttpStatusCode.Conflict } =>
                DemoActionResult.Failure(
                    "conflict",
                    "The operation conflicted with existing demo data. Refresh and try again."),

            ZenmeterManagementApiException { StatusCode: (int)HttpStatusCode.Conflict } =>
                DemoActionResult.Failure(
                    "conflict",
                    "The operation conflicted with existing demo data. Refresh and try again."),

            FastSpringApiRequestException api =>
                DemoActionResult.Failure(
                    "fastspring_request_failed",
                    BuildFastSpringMessage(api.ResponseBody)),

            _ => DemoActionResult.Failure(fallbackCode, fallbackMessage)
        };

    private static string BuildFastSpringMessage(string responseBody)
    {
        var message = ExtractFastSpringMessage(responseBody);
        return string.IsNullOrWhiteSpace(message)
            ? "FastSpring rejected the request."
            : $"FastSpring rejected the request: {message}";
    }

    private static string? ExtractFastSpringMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        var trimmed = responseBody.Trim();
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return ExtractFastSpringMessage(document.RootElement) ?? trimmed;
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static string? ExtractFastSpringMessage(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var message = ExtractFastSpringMessage(item);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "message", "errorMessage", "reason", "detail" })
        {
            if (element.TryGetProperty(propertyName, out var property))
            {
                var message = ExtractFastSpringMessage(property);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }

        foreach (var propertyName in new[] { "error", "errors" })
        {
            if (element.TryGetProperty(propertyName, out var errors))
            {
                var message = ExtractFieldErrorMessage(errors);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }

        // Subscription updates answer with a per-subscription result list, and the failing entry
        // carries the error map handled above.
        return element.TryGetProperty("subscriptions", out var subscriptions)
            ? ExtractFastSpringMessage(subscriptions)
            : null;
    }

    /// <summary>
    /// Reads a message out of a FastSpring error map.
    /// </summary>
    /// <remarks>
    /// The properties of such a map are the rejected fields, for example
    /// <c>{"error": {"subscription": "Subscription update is not allowed."}}</c>, so the message
    /// cannot be found by property name and is read from the values instead. This stays scoped to
    /// error maps on purpose: probing values of arbitrary objects would surface unrelated payload
    /// fields, such as identifiers, as if they were messages for the user.
    /// </remarks>
    private static string? ExtractFieldErrorMessage(JsonElement element)
    {
        var message = ExtractFastSpringMessage(element);
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var itemMessage = ExtractFieldErrorMessage(item);
                if (!string.IsNullOrWhiteSpace(itemMessage))
                {
                    return itemMessage;
                }
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}
