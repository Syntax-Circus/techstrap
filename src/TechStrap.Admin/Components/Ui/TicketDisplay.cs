using System.Globalization;
using Microsoft.Extensions.Logging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Maps the API's string vocabulary (Contracts carries no enums) onto the presentation enums and display text. An unknown status or
/// priority never throws, because one bad row must not blank a whole list: it falls back to the most neutral value (<see cref="StampStatus.Open"/>,
/// <see cref="PriorityLevel.Normal"/>) and logs a warning with the value (a status word, not personal data), so a contract change is noticed.
/// </summary>
internal static class TicketDisplay
{
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;
    private const int DaysPerWeek = 7;
    private const double BytesPerKilobyte = 1024;

    /// <summary>The stamp for a ticket. The spam flag wins over the status (the status is unchanged underneath, D-024).</summary>
    public static StampStatus Stamp(string status, bool isSpam, ILogger? logger = null)
    {
        if (isSpam)
        {
            return StampStatus.Spam;
        }

        switch (status)
        {
            case TicketStatuses.New:
                return StampStatus.New;
            case TicketStatuses.Open:
                return StampStatus.Open;
            case TicketStatuses.Pending:
                return StampStatus.Pending;
            case TicketStatuses.Solved:
                return StampStatus.Solved;
            case TicketStatuses.Closed:
                return StampStatus.Closed;
            default:
                logger?.LogWarning("Unknown ticket status {Status}; showing it as Open.", status);
                return StampStatus.Open;
        }
    }

    public static PriorityLevel Priority(string priority, ILogger? logger = null)
    {
        switch (priority)
        {
            case TicketPriorities.Urgent:
                return PriorityLevel.Urgent;
            case TicketPriorities.High:
                return PriorityLevel.High;
            case TicketPriorities.Normal:
                return PriorityLevel.Normal;
            case TicketPriorities.Low:
                return PriorityLevel.Low;
            default:
                logger?.LogWarning("Unknown ticket priority {Priority}; showing it as Normal.", priority);
                return PriorityLevel.Normal;
        }
    }

    /// <summary>
    /// "just now", "5 min ago", "3 h ago", "2 d ago", then the date in <paramref name="zone"/> (UTC when it is null). The caller supplies <paramref name="now"/> (a <see cref="TimeProvider"/>), so tests control it.
    /// Only the date depends on the zone: how long ago something happened is the same instant everywhere.
    /// </summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset now, TimeZoneInfo? zone = null)
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
            : TimeZoneInfo.ConvertTime(when, zone ?? TimeZoneInfo.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The absolute time for a tooltip. In UTC (or with no zone) it is "2026-10-04 11:55 UTC". In another zone it is the local time first and the UTC time after it, so the instant is never in doubt:
    /// "2026-10-04 12:55 Europe/London, 2026-10-04 11:55 UTC".
    /// </summary>
    public static string Absolute(DateTimeOffset when, TimeZoneInfo? zone = null)
    {
        var utc = when.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        if (zone is null || zone.Equals(TimeZoneInfo.Utc))
        {
            return utc;
        }

        var local = TimeZoneInfo.ConvertTime(when, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{local} {zone.Id}, {utc}");
    }

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
