namespace TechStrap.Application.Tickets.AutoClose;

/// <summary>Auto-close (D-008, D-037). Days comes from the flat key TECHSTRAP_AUTOCLOSE_DAYS; the rest from section AutoClose.</summary>
public sealed class AutoCloseOptions
{
    public const string DaysKey = "TECHSTRAP_AUTOCLOSE_DAYS";
    public const string SectionName = "AutoClose";
    public int Days { get; set; } = 7;               // 1..365
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;   // 1..1440
    public int BatchSize { get; set; } = 50;         // 1..Paging.MaxBatchSize
}
