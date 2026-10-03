namespace TechStrap.Domain.Rules;

/// <summary>Domain timestamps are whole microseconds, the resolution of Postgres <c>timestamptz</c>, so a value survives a round trip unchanged.</summary>
internal static class DomainTime
{
    internal const long MicrosecondTicks = 10;

    public static DateTimeOffset Now(TimeProvider clock) => Truncate(clock.GetUtcNow());

    public static DateTimeOffset Truncate(DateTimeOffset value) => value.AddTicks(-(value.Ticks % MicrosecondTicks));
}
