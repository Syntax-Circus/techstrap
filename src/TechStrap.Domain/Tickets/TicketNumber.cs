using System.Text.RegularExpressions;

namespace TechStrap.Domain.Tickets;

/// <summary>
/// The immutable, human-facing ticket number, e.g. <c>ACME-142</c> (D-009): the product's number prefix at creation time
/// plus that product's sequence. It keeps its original prefix when the ticket moves to another product.
/// </summary>
public readonly partial record struct TicketNumber
{
    public const string PrefixPattern = "^[A-Z][A-Z0-9]{1,9}$";
    private const char Separator = '-';

    private TicketNumber(string prefix, long sequence)
    {
        Prefix = prefix;
        Sequence = sequence;
    }

    public string Prefix { get; }

    public long Sequence { get; }

    public static bool IsValidPrefix(string? prefix) => prefix is not null && PrefixRegex().IsMatch(prefix);

    public static DomainResult<TicketNumber> Create(string prefix, long sequence)
    {
        if (!IsValidPrefix(prefix))
        {
            return DomainErrors.Validation("ticket-number-prefix-invalid", "A number prefix is 2 to 10 upper-case letters or digits and starts with a letter.", "prefix");
        }

        if (sequence < 1)
        {
            return DomainErrors.Validation("ticket-number-sequence-invalid", "A ticket sequence starts at 1.", "sequence");
        }

        return DomainResult<TicketNumber>.Ok(new TicketNumber(prefix, sequence));
    }

    /// <summary>Parses "ACME-142" (case-insensitive, as customers type it into a search box).</summary>
    public static bool TryParse(string? value, out TicketNumber number)
    {
        number = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var split = text.LastIndexOf(Separator);
        if (split <= 0 || !long.TryParse(text.AsSpan(split + 1), out var sequence))
        {
            return false;
        }

        var result = Create(text[..split].ToUpperInvariant(), sequence);
        if (result.IsFailure)
        {
            return false;
        }

        number = result.Value;
        return true;
    }

    public override string ToString() => $"{Prefix}{Separator}{Sequence}";

    [GeneratedRegex(PrefixPattern, RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();
}
