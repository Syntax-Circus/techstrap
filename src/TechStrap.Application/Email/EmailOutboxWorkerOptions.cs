namespace TechStrap.Application.Email;

public sealed class EmailOutboxWorkerOptions
{
    public const string SectionName = "EmailOutbox";

    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 20;

    /// <summary>Covers sending one whole batch; a crashed worker's rows are reclaimed after it.</summary>
    public int LeaseSeconds { get; set; } = 120;

    public string? WorkerId { get; set; }
}
