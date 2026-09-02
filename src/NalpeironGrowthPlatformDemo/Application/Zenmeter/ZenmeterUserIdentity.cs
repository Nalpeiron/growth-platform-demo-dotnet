using System.Globalization;
using System.Text;

namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

public static class ZenmeterUserIdentity
{
    public const int MaxExternalUserIdLength = 50;

    public static ZenmeterUserDetails FromInput(ZenmeterUserInput input)
    {
        var firstName = input.FirstName.Trim();
        var lastName = input.LastName.Trim();
        var email = input.Email.Trim();
        var externalUserId = BuildExternalUserId(firstName, lastName, email);

        return new ZenmeterUserDetails(externalUserId, firstName, lastName, email);
    }

    private static string BuildExternalUserId(
        string firstName,
        string lastName,
        string email)
    {
        var candidate = ToIdPart($"{firstName}-{lastName}");
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = ToIdPart(email.Split('@', 2)[0]);
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ArgumentException(
                "User details must contain characters that can be used to build an external user id.",
                nameof(firstName));
        }

        return candidate.Length <= MaxExternalUserIdLength
            ? candidate
            : candidate[..MaxExternalUserIdLength].TrimEnd('-');
    }

    private static string ToIdPart(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalized.Length);
        var separatorPending = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && result.Length > 0)
                {
                    result.Append('-');
                }

                result.Append(character);
                separatorPending = false;
            }
            else
            {
                separatorPending = result.Length > 0;
            }
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
