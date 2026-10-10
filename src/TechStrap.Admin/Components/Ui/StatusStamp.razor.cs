using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// A rubber-stamp status badge. The status is always the word itself plus a shape class, never color alone.
/// Queue stamps are straight with one border; ticket stamps are tilted and may play the stamp-down animation.
/// </summary>
public partial class StatusStamp
{
    private const string Base = "ts-stamp";

    [Parameter, EditorRequired]
    public StampStatus Status { get; set; }

    [Parameter]
    public StampVariant Variant { get; set; } = StampVariant.Queue;

    /// <summary>Play the stamp-down animation (ticket variant only), used when the status has just changed.</summary>
    [Parameter]
    public bool Animate { get; set; }

    private string CssClass
    {
        get
        {
            var css = $"{Base} {Base}--{Status.ToString().ToLowerInvariant()} {Base}--{Variant.ToString().ToLowerInvariant()}";
            return Animate && Variant == StampVariant.Ticket ? $"{css} {Base}--pop" : css;
        }
    }

    // The queue shows "Spam?" (a suggestion to confirm); the ticket view shows the settled "Spam".
    private string Label => Status == StampStatus.Spam && Variant == StampVariant.Queue ? "Spam?" : Status.ToString();
}
