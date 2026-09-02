using System.Text;

namespace NalpeironGrowthPlatformDemo.Domain;

/// <summary>
/// Builds neutral reference ids for objects created in the platform during a demo, all prefixed
/// with <see cref="Prefix"/> so demo data is easy to find and bulk-delete in the admin.
/// Lengths respect the API limits (customer accountRefId max 32, order orderRefId max 50).
/// </summary>
public static class ReferenceId
{
    public const string Prefix = "_demo-z2-";

    public static string ForCustomer() =>
        $"{Prefix}{Guid.NewGuid():N}"[..32];

    public static string ForOrder(string customerName)
    {
        var id = $"{Prefix}{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Slug(customerName)}";
        return id.Length > 50 ? id[..50] : id;
    }

    public static string ForTopUp(string customerName)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var id = $"{Prefix}tu-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{nonce}-{Slug(customerName)}";
        return id.Length > 50 ? id[..50] : id;
    }

    public static string Slug(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }

        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "customer" : slug;
    }
}