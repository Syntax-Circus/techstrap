namespace TechStrap.Application.Tickets.Customer;

/// <summary>Lost-link recovery limits (D-037). Bound from section LostLink.</summary>
public sealed class LostLinkOptions
{
    public const string SectionName = "LostLink";
    public int MaxLinks { get; set; } = 5;               // 1..10
    public int PerAddressLimit { get; set; } = 3;        // 1..20
    public int PerAddressWindowMinutes { get; set; } = 60; // 1..1440
}
