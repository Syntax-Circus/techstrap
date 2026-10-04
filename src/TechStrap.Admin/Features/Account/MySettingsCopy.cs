namespace TechStrap.Admin.Features.Account;

/// <summary>The copy of My settings: alert, keyboard, theme and public display name. The example and the helper line of the display name come from UX-BRIEF-admin word for word.</summary>
public static class MySettingsCopy
{
    public const int PublicNameMaxLength = 60;

    public const string Heading = "My settings";

    public const string AlertsHeading = "Email alerts";
    public const string AlertsHelp = "Get an email when a new ticket arrives for a product.";
    public const string NoProducts = "No active products yet";
    public const string AlertsLoading = "Loading your alert settings";
    public const string AlertsLoadFailed = "Couldn't load your alert settings.";
    public const string AlertsSaved = "Saved your alert settings";
    public const string AlertsUncertain = "Couldn't confirm that your alert settings were saved. Reload to see what is saved.";
    public const string Reload = "Reload";

    public const string KeyboardHeading = "Keyboard shortcuts";
    public const string KeyboardLabel = "Keyboard shortcuts";
    public const string KeyboardHelp = "Single-key shortcuts such as j, k, r and u. This is remembered in this browser.";
    public const string ThemeHeading = "Theme";
    public const string ThemeHelp = "Remembered in this browser.";
    public const string ThemeAuto = "Auto (follows your device)";
    public const string ThemeLight = "Light";
    public const string ThemeDark = "Dark";

    public const string NameHeading = "Public display name";
    public const string NameLabel = "Public display name (optional)";
    public const string NameHelp = "Customers never see your email address.";
    public const string NameSaved = "Saved.";
    public const string NameTooLong = "Use 60 characters or fewer.";
    public const string NameInvalid = "Use a name without @, so it can't be mistaken for an email address.";
    public const string NameUncertain = "The save may have gone through. Reload the page to see what is saved before you try again.";

    // The placeholder stands in for a product when none is active yet.
    public const string GenericProduct = "[product]";

    public static string AlertLabel(string product) => $"New tickets in {product}";

    public static string AlertsFailed(string reason) => $"Couldn't save your alert settings. Nothing was changed. {reason}";

    public static string NameFailed(string reason) => $"Couldn't save your public display name. Nothing was changed. {reason}";

    public static string Preview(string line) => $"Customers see: {line}";
}
