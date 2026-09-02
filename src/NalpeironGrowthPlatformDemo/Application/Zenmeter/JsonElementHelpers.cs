using System.Text.Json;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal static class JsonElementHelpers
{
    public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public static string Truncate(string value, int maxLength = 1_000) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
