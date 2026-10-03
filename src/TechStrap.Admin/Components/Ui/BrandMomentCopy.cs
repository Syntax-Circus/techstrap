namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Copy for the Admin brand moments, where humour is allowed (docs/BRAND.md section 3). Kept apart from <see cref="UiCopy"/>,
/// which is for failures that block work and never jokes. Defined once so the running page and the style guide cannot drift apart.
/// </summary>
public static class BrandMomentCopy
{
    public const string NotFoundTitle = "ERROR 404 — not found";
    public const string NotFoundHeading = "This page fell out of its strap.";
    public const string NotFoundBody = "The address you followed doesn't match any page here. It may have moved, or the link may have a typo.";
    public const string NotFoundLinkText = "Back to the queue";
}
