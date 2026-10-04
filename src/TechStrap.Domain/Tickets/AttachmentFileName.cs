using System.Globalization;
using System.Text;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tickets;

/// <summary>The one rule for the display name stored with an attachment (D-039). The store saves this form, so anything that compares a
/// customer-supplied name with a stored one must sanitise the customer's first.</summary>
public static class AttachmentFileName
{
    public const string Fallback = "attachment";

    public static string Sanitize(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
            {
                builder.Append(c);
            }
        }

        name = builder.ToString().Trim();
        if (name is "" or "." or "..")
        {
            return Fallback;
        }

        if (name.Length > DomainLimits.FileNameMaxLength)
        {
            var extension = Path.GetExtension(name);
            name = extension.Length < DomainLimits.FileNameMaxLength / 2
                ? string.Concat(name.AsSpan(0, SafeCut(name, DomainLimits.FileNameMaxLength - extension.Length)), extension)
                : name[..SafeCut(name, DomainLimits.FileNameMaxLength)];
        }

        return name;
    }

    /// <summary>The cut length, backed off by one when it would split a surrogate pair.</summary>
    private static int SafeCut(string name, int length) =>
        length > 0 && char.IsHighSurrogate(name[length - 1]) ? length - 1 : length;
}
