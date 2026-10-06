using System.Net.Mail;
using TechStrap.Contracts.Intake;

namespace TechStrap.Portal.Forms;

/// <summary>The one check of an email address the Portal's forms make before they ask the API (which is the authority): present, within <see cref="IntakeLimits.EmailMaxLength"/>, and one plain dotted address.</summary>
public static class EmailRules
{
    /// <summary>The error for this value, or null when it is acceptable. The value is judged trimmed.</summary>
    public static FormError? Check(string? value)
    {
        var email = value?.Trim() ?? string.Empty;
        if (email.Length == 0)
        {
            return new FormError(FormFields.Email, "email-required", FormCopy.For("email-required"));
        }

        return email.Length > IntakeLimits.EmailMaxLength || !LooksLikeAnAddress(email)
            ? new FormError(FormFields.Email, "email-invalid", FormCopy.For("email-invalid"))
            : null;
    }

    // Not a full address grammar (the API decides): one address, no display name, a dotted domain.
    private static bool LooksLikeAnAddress(string text) =>
        MailAddress.TryCreate(text, out var address) && address.Address == text && address.Host.Contains('.', StringComparison.Ordinal);
}
