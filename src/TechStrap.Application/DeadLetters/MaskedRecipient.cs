namespace TechStrap.Application.DeadLetters;

internal static class MaskedRecipient
{
    private const string Hidden = "***";

    /// <summary>"ann@example.com" gives "a***@example.com"; a one-character local part gives "***@example.com"; null, blank, or anything that is not exactly one address with a non-empty local part and domain gives "***".</summary>
    public static string Mask(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return Hidden;
        }

        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == address.Length - 1 || address.IndexOf('@', at + 1) >= 0)
        {
            return Hidden;
        }

        var domain = address[(at + 1)..];
        return at == 1 ? $"{Hidden}@{domain}" : $"{address[0]}{Hidden}@{domain}";
    }
}
