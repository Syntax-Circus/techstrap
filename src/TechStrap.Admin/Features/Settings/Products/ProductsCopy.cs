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

    public const string ColumnName = "Name";
    public const string ColumnKey = "Key";
    public const string ColumnPrefix = "Prefix";
    public const string ColumnPortalHost = "Portal host";
    public const string ColumnStatus = "Status";
    public const string ColumnActions = "Actions";

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
    public const string PortalHostLabel = "Portal host";
    public const string PortalHostHelp = "Optional. The product's own support hostname, e.g. support.example.com. Set up DNS and the proxy site first; saving the host switches links and redirects at once.";
    public const string PortalHostInvalid = "Use a hostname such as support.example.com: letters, digits and hyphens, no scheme, port or path.";
    public const string PortalHostTaken = "Another product already uses this hostname.";
    public const string SkinHeading = "Appearance (advanced)";
    public const string SkinLabel = "Skin (JSON)";
    public const string SkinHelp = "Optional. A JSON object of colours, fonts and presets that restyles this product's portal; empty it to remove the skin. See docs/skins/README.md for a worked example and the list of tokens.";
    public const string SkinInvalid = "This is not a valid skin: use a JSON object of known members, up to 2000 characters.";

    public static string SkinTokenInvalid(string token) => $"The skin value \"{token}\" is not valid. Colours are #RRGGBB; see docs/skins/README.md for the allowed values.";

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

    public const string TaglineLabel = "Tagline";
    public const string TaglineHelp = "One line about the product, shown on the landing page card. Optional.";
    public const string TaglineInvalid = "Use one line of plain text, 160 characters or fewer.";
    public const string ListedLabel = "Listed on the landing page";
    public const string ListedHelp = "When the portal lists products on its front page, this product has a card. Unlisting hides the card only: the product's own pages, host and help articles stay reachable.";
    public const string ColumnListed = "Landing";
    public const string Listed = "Listed";
    public const string Hidden = "Hidden";
    public const string LogoUploadLabel = "Upload a logo";
    public const string LogoUploadHelp = "PNG, JPEG or WebP, up to 1 MB. An uploaded logo replaces the logo address wherever the logo is shown.";
    public const string LogoUploading = "Uploading\u2026";
    public const string LogoRemove = "Remove uploaded logo";
    public const string LogoUploadAfterSave = "Save the product first, then upload a logo from its editor.";
    public const string LogoTypeNotAllowed = "Choose a PNG, JPEG or WebP image.";
    public const string LogoTooLarge = "That image is too large. Logos can be up to 1 MB.";
    public const string LogoReadFailed = "That image could not be read. Choose it again.";
    public const string LogoUncertain = "The upload may have gone through. Reload the product to see its logo.";
    public const string LogoFailed = "The logo could not be uploaded.";

    public static string Saved(string name) => $"Saved {name}";

    public static string Created(string name) => $"Created {name}";
}
