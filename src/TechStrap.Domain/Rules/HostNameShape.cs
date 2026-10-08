namespace TechStrap.Domain.Rules;

/// <summary>
/// The shape of a public hostname a product can be served on (D-050): ASCII letters, digits and hyphens in dot-separated labels, at least two labels,
/// no scheme, port, path, user info or whitespace. A name is stored lower-case.
/// </summary>
public static class HostNameShape
{
    private const int LabelMaxLength = 63;

    /// <summary>
    /// Trims and lower-cases <paramref name="input"/>. A blank input succeeds with <paramref name="host"/> null (it clears the host); a name of the wrong shape
    /// returns false with a null host.
    /// </summary>
    public static bool TryNormalize(string? input, out string? host)
    {
        host = null;
        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (trimmed.Length > DomainLimits.HostNameMaxLength || !HasValidShape(trimmed))
        {
            return false;
        }

        host = trimmed.ToLowerInvariant();
        return true;
    }

    /// <summary>True when <paramref name="host"/> is already in the normalised form <see cref="TryNormalize"/> produces (and is not blank).</summary>
    public static bool IsWellFormed(string host) =>
        TryNormalize(host, out var normalised) && normalised is not null && string.Equals(normalised, host, StringComparison.Ordinal);

    // The ASCII check runs before lower-casing, so characters that fold to ASCII (the Kelvin sign, for one) cannot slip through.
    private static bool HasValidShape(string value)
    {
        var labels = 0;
        var labelLength = 0;
        var previous = '.';
        foreach (var c in value)
        {
            if (c == '.')
            {
                if (labelLength == 0 || previous == '-')
                {
                    return false;
                }

                labels++;
                labelLength = 0;
            }
            else if (IsLabelCharacter(c) && !(c == '-' && labelLength == 0))
            {
                if (++labelLength > LabelMaxLength)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            previous = c;
        }

        return labelLength > 0 && previous != '-' && labels >= 1;
    }

    private static bool IsLabelCharacter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';
}
