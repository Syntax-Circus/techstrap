namespace TechStrap.Portal.Components;

/// <summary>
/// The words of the shell pages. Plain copy, no humor (BRAND.md): TechStrap's name appears nowhere here, because the Portal is the product's, and the only TechStrap line is the
/// "Powered by TechStrap" footer. A page references these; it never writes a sentence of its own.
/// </summary>
public static class ShellCopy
{
    // The root page: the neutral copy (TECHSTRAP_PORTAL_LANDING=Neutral, or nothing to list), and the landing page (Products, D-052). Inactive products never appear.
    public const string RootTitle = "Support";
    public const string RootHeading = "Support";
    public const string RootIntro = "Open the help page of your product, or follow a ticket with the link in your email.";
    public const string LandingHeading = "Support";
    public const string LandingIntro = "Choose your product to find help articles or contact support.";

    // A product page.
    public const string HomeHeading = "How can we help?";
    public const string SearchLabel = "Search help articles";
    public const string SearchButton = "Search";
    public const string ContactSupport = "Contact support";
    public const string LostLinkPrompt = "Lost your ticket link?";

    public const string SkipToMain = "Skip to main content";

    // The landmarks (the accessible names of the two navigation regions).
    public const string NavLabel = "Main";
    public const string NavHelp = "Help articles";
    public const string NavContact = "Contact support";
    public const string FooterNavLabel = "More help";

    // The calm failure state of a product page.
    public const string UnavailableTitle = "This page could not be loaded.";
    public const string UnavailableRetry = "Try again";

    // The neutral system pages.
    public const string NotFoundTitle = "Not found";
    public const string NotFoundHeading = "Page not found";
    public const string ErrorTitle = "Error";
    public const string ErrorHeading = "Something went wrong.";
    public const string ErrorText = "We could not finish that request. Try again in a minute; if it keeps happening, contact support.";
    public const string BackToStart = "Back to the start";

    public static string ProductTitle(string productName) => $"{productName} Support";
}
