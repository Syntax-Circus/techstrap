using System.Globalization;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Maps the API's string vocabulary (Contracts carries no enums) onto the presentation enums and display text. An unknown status or
/// priority throws: both sets are closed, so a new value is a contract change that must fail loudly in the error boundary, not render as a guess.
/// </summary>
internal static class TicketDisplay
{
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;
    private const int DaysPerWeek = 7;
    private const double BytesPerKilobyte = 1024;

    /// <summary>The stamp for a ticket. The spam flag wins over the status (the status is unchanged underneath, D-024).</summary>
    public static StampStatus Stamp(string status, bool isSpam) => isSpam
        ? StampStatus.Spam
        : status switch
        {
            TicketStatuses.New => StampStatus.New,
            TicketStatuses.Open => StampStatus.Open,
            TicketStatuses.Pending => StampStatus.Pending,
            TicketStatuses.Solved => StampStatus.Solved,
            TicketStatuses.Closed => StampStatus.Closed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown ticket status."),
        };

    public static PriorityLevel Priority(string priority) => priority switch
    {
        TicketPriorities.Urgent => PriorityLevel.Urgent,
        TicketPriorities.High => PriorityLevel.High,
        TicketPriorities.Normal => PriorityLevel.Normal,
        TicketPriorities.Low => PriorityLevel.Low,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown ticket priority."),
    };

    /// <summary>"just now", "5 min ago", "3 h ago", "2 d ago", then the UTC date. The caller supplies <paramref name="now"/> (a <see cref="TimeProvider"/>), so tests control it.</summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (delta.TotalMinutes < MinutesPerHour)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalMinutes} min ago");
        }

        if (delta.TotalHours < HoursPerDay)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalHours} h ago");
        }

        return delta.TotalDays < DaysPerWeek
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalDays} d ago")
            : when.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>The absolute time for a tooltip. Always UTC and labelled so: converting to the agent's zone needs the browser's zone (recorded gap, PHASE-07c).</summary>
    public static string Absolute(DateTimeOffset when) => when.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Up to two initials for an avatar: "Sam Ortiz" is "SO", "sam" is "S", nothing is "?".</summary>
    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0
            ? "?"
            : string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / BytesPerKilobyte:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (BytesPerKilobyte * BytesPerKilobyte):0.#} MB"),
    };
}
