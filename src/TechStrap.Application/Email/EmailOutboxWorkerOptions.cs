namespace TechStrap.Application.Email;

public sealed class EmailOutboxWorkerOptions
{
    public const string SectionName = "EmailOutbox";

    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// Covers sending one whole batch (default 900 = 20 rows x the 30 s total send timeout = 600 s, plus margin);
    /// a crashed worker's rows are reclaimed after it.
    /// </summary>
    public int LeaseSeconds { get; set; } = 900;

    public string? WorkerId { get; set; }
}
