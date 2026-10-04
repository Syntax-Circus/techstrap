namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>The copy of the product list and the product editor.</summary>
public static class ProductsCopy
{
    public const string Heading = "Products";
    public const string NewProduct = "New product";
    public const string Loading = "Loading products";
    public const string LoadFailed = "Couldn't load products.";
    public const string NoProducts = "No products yet";
    public const string NoProductsHint = "Create the first product to give it a portal, branding and API keys.";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Edit = "Edit";
    public const string ApiKeys = "API keys";
    public const string BackToProducts = "Products";

    public const string EditorNewTitle = "New product";
    public const string EditorLoading = "Loading product";
    public const string EditorLoadFailed = "Couldn't load this product.";
    public const string EditorGone = "This product no longer exists.";

    public const string KeyLabel = "Key";
    public const string KeyHelp = "Letters, numbers and hyphens. It can't be changed once the product exists.";
    public const string NameLabel = "Name";
    public const string NumberPrefixLabel = "Ticket number prefix";
    public const string NumberPrefixHelp = "2 to 10 capital letters or numbers, such as ORB. It can't be changed once the product exists.";
    public const string BrandingHeading = "Branding";
    public const string DisplayNameLabel = "Display name";
    public const string DisplayNameHelp = "The name customers see in the portal and in emails.";
    public const string LogoLabel = "Logo address";
    public const string LogoHelp = "A full https:// address of an image. Leave it blank for no logo.";
    public const string AccentLabel = "Accent colour";
    public const string AccentHelp = "A # and six hex digits, such as #1D4ED8. Leave it blank for the default.";
    public const string FromLabel = "Email from address";
    public const string ReplyToLabel = "Email reply-to address";
    public const string ActiveLabel = "Active";
    public const string ActiveHelp = "Inactive products are hidden from the agents' product lists.";
    public const string PreviewHeading = "Preview";

    public const string Save = "Save product";
    public const string Create = "Create product";
    public const string Saving = "Saving\u2026";
    public const string Unsaved = "Unsaved changes";

    public const string KeyRequired = "Enter a key.";
    public const string KeyInvalid = "Use lower-case letters, numbers and single hyphens, up to 40 characters.";
    public const string NameRequired = "Enter a name.";
    public const string NameTooLong = "Use 100 characters or fewer.";
    public const string NumberPrefixInvalid = "Use 2 to 10 capital letters or numbers, starting with a letter.";
    public const string DisplayNameRequired = "Enter the name customers see.";
    public const string LogoInvalid = "Use a full https:// address for the logo.";
    public const string AccentInvalid = "Use a colour like #1D4ED8: a # and six hex digits.";
    public const string EmailInvalid = "Enter a valid email address.";

    public const string ConflictTitle = "This product changed since you opened it.";
    public const string ConflictKept = "Your edits are still on screen. Reload shows the saved version and drops them.";
    public const string Reload = "Reload";
    public const string SaveUncertain = "The save may have gone through. Reload to see the saved version before you save again.";
    public const string CreateUncertain = "We could not confirm the product was created. Check the products list before you create it again.";
    public const string OpenList = "Open the products list";
    public const string ProductKeyTaken = "Another product already uses this key or ticket number prefix.";

    public static string Saved(string name) => $"Saved {name}";

    public static string Created(string name) => $"Created {name}";
}
