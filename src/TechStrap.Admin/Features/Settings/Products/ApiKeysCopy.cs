using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>The copy of the API keys panel, the new-key dialog and the revoke confirmation. The two kind explanations come from UX-BRIEF-admin word for word.</summary>
public static class ApiKeysCopy
{
    public const string Heading = "API keys";
    public const string Loading = "Loading API keys";
    public const string LoadFailed = "Couldn't load the API keys.";
    public const string NoKeys = "No API keys yet";
    public const string NoKeysHint = "An app needs a key to create tickets for this product. Choose the kind that fits where the key will live:";
    public const string ProductGone = "This product no longer exists.";

    public const string TrustedLine = "Trusted: server-side only, may set external user ref and trusted metadata";
    public const string PublicLine = "Public: safe to embed in a client app, create-only, rate limited, metadata treated as untrusted";

    public const string KindLabel = "Kind";
    public const string KindPlaceholder = "Choose a kind";
    public const string KindRequired = "Choose a kind.";
    public const string LabelLabel = "Label (optional)";
    public const string LabelHelp = "Helps you tell keys apart, such as the app that uses it. Up to 100 characters.";
    public const string LabelTooLong = "Use 100 characters or fewer.";
    public const string Create = "Create key";
    public const string Creating = "Creating\u2026";

    public const string ColumnLabel = "Label";
    public const string ColumnKind = "Kind";
    public const string ColumnKey = "Key";
    public const string ColumnCreated = "Created";
    public const string ColumnLastUsed = "Last used";
    public const string ColumnStatus = "Status";
    public const string NoLabel = "No label";
    public const string NeverUsed = "Never used";
    public const string Active = "Active";
    public const string Revoked = "Revoked";
    public const string Revoke = "Revoke";

    public const string NewKeyTitle = "Your new API key";
    public const string SecretLabel = "New API key";
    public const string Copy = "Copy";
    public const string Copied = "Copied to the clipboard.";
    public const string CopyFailed = "Couldn't copy. The key is selected: press Ctrl+C to copy it.";
    public const string Stored = "I have stored this key";
    public const string StoreFirst = "Tick \"I have stored this key\" before you close this window.";
    public const string Done = "Done";

    public const string RevokeBody = "Apps using this key will stop working.";
    public const string RevokeIrreversible = "This can't be undone.";
    public const string RevokeConfirm = "Revoke key";
    public const string RevokeUncertain = "The revoke may have gone through. Reload the list to check before you try again.";
    public const string CreateUncertain = "The key may have been created, but if the answer was lost its secret can't be shown. Reload the list. If you see a key you never copied, revoke it and create another.";
    public const string ReloadList = "Reload list";
    public const string KeyGone = "That key no longer exists.";

    public static string Kind(string kind) => kind == ApiKeyKinds.Trusted ? TrustedLine : PublicLine;

    public static string ShownOnce(string product) =>
        $"This is the only time the key for {product} is shown. Copy it and keep it somewhere safe: TechStrap can't show it again.";

    public static string RevokeTitle(string name) => $"Revoke {name}?";

    public static string RevokeSubject(string name, string prefix, string kind, string product) => $"{name} ({prefix}), a {kind} key for {product}.";

    public static string RevokeFailed(string reason) => $"Couldn't revoke the key. Nothing was changed. {reason}";

    public static string RevokedMessage(string name) => $"Revoked {name}";

    public static string CreatedMessage(string kind, string product) => $"Created a {kind} key for {product}";
}
