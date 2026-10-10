namespace TechStrap.Portal.Settings;

/// <summary>The two values of <c>TECHSTRAP_PORTAL_LANDING</c> (D-052): the neutral root of D-045, or a list of the products whose <c>ListedOnLanding</c> is true.</summary>
public static class PortalLandingModes
{
    public const string Neutral = "Neutral";
    public const string Products = "Products";

    public static bool IsKnown(string? value) =>
        string.Equals(value?.Trim(), Neutral, StringComparison.OrdinalIgnoreCase) || string.Equals(value?.Trim(), Products, StringComparison.OrdinalIgnoreCase);
}
