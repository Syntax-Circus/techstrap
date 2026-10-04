using System.Text.RegularExpressions;

namespace TechStrap.Application.Tickets.Customer;

internal static partial class CustomerEmailAddress
{
    // Mirrors Guard.Email in the Domain (pattern and 320 limit); CustomerEmailAddressTests pins the two together.
    private const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
    private const int EmailMaxLength = 320;

    /// <summary>Trims and lower-cases; true when the address has the shape the Domain accepts (same rule as Guard.Email, max 320).</summary>
    public static bool TryNormalize(string? value, out string email)
    {
        var text = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text) || text.Length > EmailMaxLength || !EmailRegex().IsMatch(text))
        {
            email = string.Empty;
            return false;
        }

        email = text;
        return true;
    }

    [GeneratedRegex(EmailPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex EmailRegex();
}
