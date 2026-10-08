namespace TechStrap.Portal.Hosting;

/// <summary>The lifetimes of the host map (PHASE-11e). Constants, not settings (D-043): the map is driven by the API, and a change of host shows within a minute.</summary>
public static class ProductHostMapOptions
{
    /// <summary>How long a read of the product list is trusted before the next request reads it again.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    /// <summary>The shortest time between two reads of the product list: a host the map does not know (a typo, a scanner) can cost the API one call in ten seconds, never one per request.</summary>
    public static readonly TimeSpan MissRefreshInterval = TimeSpan.FromSeconds(10);
}
