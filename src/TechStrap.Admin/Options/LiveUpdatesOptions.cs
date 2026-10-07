namespace TechStrap.Admin.Options;

/// <summary>
/// The kill switch for live updates (PHASE-10b): <c>LiveUpdates:Enabled</c>, env <c>LIVEUPDATES__ENABLED</c>, on by default. Off, the Admin never opens a hub connection and draws no indicator, banner or presence bar.
/// The hub address is not a setting: it is <c>API__BASEURL</c> plus the hub path. Backoff, debounce and throttle are constants.
/// </summary>
public sealed class LiveUpdatesOptions
{
    public const string SectionName = "LiveUpdates";

    public bool Enabled { get; set; } = true;
}
