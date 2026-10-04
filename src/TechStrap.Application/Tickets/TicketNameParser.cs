using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

/// <summary>Parses wire names into enums: case-insensitive, surrounding whitespace ignored, only defined names (never digits).</summary>
internal static class TicketNameParser
{
    public static bool TryStatus(string? value, out TicketStatus status) => TryName(value, out status);

    public static bool TryPriority(string? value, out TicketPriority priority) => TryName(value, out priority);

    /// <summary>A null or blank view is the default view.</summary>
    public static bool TryView(string? value, out TicketView view) =>
        TryName(string.IsNullOrWhiteSpace(value) ? TicketViews.Default : value, out view);

    private static bool TryName<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var candidate in Enum.GetNames<TEnum>())
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                result = Enum.Parse<TEnum>(candidate);
                return true;
            }
        }

        return false;
    }
}
