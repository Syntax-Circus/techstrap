namespace TechStrap.Portal.Forms;

/// <summary>The words of the contact page and the received page (UX brief). The product's name is the only name on them: TechStrap's appears only in the "Powered by" footer.</summary>
public static class ContactCopy
{
    public const string Heading = "Contact support";
    public const string BrowseHelp = "Browse help articles";
    public const string NameLabel = "Your name";
    public const string EmailLabel = "Email address";
    public const string SubjectLabel = "Subject";
    public const string BodyLabel = "Message";
    public const string Submit = "Send message";

    /// <summary>The fallback inside the suggestions element, shown only without script: a plain link to the help search.</summary>
    public const string SuggestFallback = "Search the help articles first";

    // The words of the suggestions list. The script has its own fallbacks, but the page owns its copy and passes these to the element as data attributes.
    public const string SuggestOne = "1 article may help";
    public const string SuggestMany = "{0} articles may help";
    public const string SuggestNewTab = "(opens in a new tab)";

    /// <summary>The label of the honeypot field. No person sees it (it is hidden from sight and from assistive technology), but a bot that reads the markup sees an ordinary field.</summary>
    public const string HoneypotLabel = "Website";

    // The received page.
    public const string ReceivedHeading = "We have received your request.";
    public const string YourNumber = "Your ticket number is";
    public const string ReceivedNext = "We have emailed you a link. Use it to follow the conversation and reply.";
    public const string ReceivedGeneric = "We have emailed you a link to follow it. Check your inbox and your spam folder.";
    public const string LostLinkPrompt = "Can't find the email?";

    public static string Title(string productName) => $"Contact {productName} support";

    public static string ReceivedTitle(string productName) => $"Request received: {productName}";

    public static string BackTo(string productName) => $"Back to {productName}";
}
