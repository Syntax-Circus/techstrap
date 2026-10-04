namespace TechStrap.Application.Email;

/// <summary>Outbox retention (D-039). Sent and Discarded rows older than Days are deleted; the age is measured from created_at for both.</summary>
public sealed class OutboxRetentionOptions
{
    public const string SectionName = "OutboxRetention";

    public bool Enabled { get; set; } = true;

    /// <summary>1..3650.</summary>
    public int Days { get; set; } = 90;

    /// <summary>1..1440.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>1..Paging.MaxBatchSize.</summary>
    public int BatchSize { get; set; } = 500;
}
