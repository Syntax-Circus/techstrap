namespace TechStrap.Admin.Features.Kb;

/// <summary>The words of the article editor, the preview and the image button. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class KbEditorCopy
{
    public const string NewTitle = "New article";
    public const string BackToList = "Knowledge base";
    public const string Loading = "Loading article";
    public const string LoadFailed = "Couldn't load this article.";
    public const string Gone = "This article no longer exists.";

    public const string ProductLabel = "Product";
    public const string SharedOption = "Shared by every product";
    public const string ProductHelp = "Chosen once. It can't be changed after the article is created.";
    public const string CategoryLabel = "Category";
    public const string NoCategoryOption = "No category";
    public const string CategoryHelp = "Needed to publish: the portal address of an article includes its category.";
    public const string TitleLabel = "Title";
    public const string SlugLabel = "Slug";
    public const string SlugHelp = "Letters, numbers and hyphens. It is part of the article's address and can't be changed once the article exists.";
    public const string SummaryLabel = "Summary";
    public const string SummaryHelp = "One or two sentences. It is shown in search results.";
    public const string BodyLabel = "Article";

    public const string SaveNew = "Create draft";
    public const string Save = "Save";
    public const string Saving = "Saving\u2026";
    public const string Publish = "Publish";
    public const string Publishing = "Publishing\u2026";
    public const string Archive = "Archive";
    public const string Unsaved = "Unsaved changes";
    public const string SaveFirst = "Save your changes before you publish.";
    public const string ViewOnPortal = "View on portal";

    public const string TitleRequired = "Enter a title.";
    public const string TitleTooLong = "Use 200 characters or fewer.";
    public const string SlugRequired = "Enter a slug.";
    public const string SlugInvalid = "Use lower-case letters, numbers and single hyphens, up to 80 characters.";
    public const string SlugTaken = "Another article already uses this slug. Slugs are shared across products, so pick another.";
    public const string SummaryTooLong = "Use 500 characters or fewer.";
    public const string BodyRequired = "Write the article before you save it.";
    public const string BodyTooLong = "The article is too long to save.";
    public const string BodyTooComplex = "This article has too many elements (paragraphs, list items, table cells). Split it into several articles or shorten the table or list.";
    public const string CategoryScopeMismatch = "A shared article can only use a shared category.";

    public const string ConflictTitle = "This article changed since you opened it.";
    public const string ConflictKept = "Your edits are still in the form. Reloading brings in the latest version and replaces them, so copy anything you need first.";
    public const string Reload = "Reload article";
    public const string SaveUncertain = "The change may have gone through. Reload the article to check before you try again.";
    public const string CreateUncertain = "The article may have been created. Open the list to check before you try again.";
    public const string OpenList = "Open the list";
    public const string PublishUncertain = "The publish may have gone through. Reload the article to check before you try again.";
    public const string ArchiveUncertain = "The archive may have gone through. Reload the article to check before you try again.";

    public const string ArchivedReopened = "Saved. This archived article is a draft again: publish it to put it back on the portal.";
    public const string ReloadFailed = "The article could not be reloaded. Reload it before you edit again.";
    public const string CategoryGone = "That category no longer exists. Choose another.";
    public const string AlreadyPublished = "Someone published this article already. It has been reloaded.";
    public const string AlreadyArchived = "Someone archived this article already. It has been reloaded.";

    public const string ArchiveTitle = "Archive this article?";
    public const string ArchiveBody = "It is removed from the portal, from search and from the sitemap. You can edit it later, which makes it a draft again, and publish it again.";
    public const string ArchiveConfirm = "Archive article";
    public const string LeaveTitle = "Leave without saving?";
    public const string LeaveBody = "You have unsaved changes. If you leave now they are lost.";
    public const string LeaveConfirm = "Leave";
    public const string LeaveStay = "Stay";

    public const string ToolbarLabel = "Formatting";
    public const string BoldButton = "Bold";
    public const string ItalicButton = "Italic";
    public const string LinkButton = "Link";
    public const string ListButton = "List";
    public const string CodeButton = "Code";
    public const string ToolbarHint = "Each button adds its Markdown at the end of the article.";

    public const string WriteTab = "Write";
    public const string PreviewTab = "Preview";
    public const string PreviewLabel = "Preview of the article";
    public const string PreviewEmpty = "Nothing to preview yet.";
    public const string PreviewWorking = "Updating the preview";
    public const string PreviewTooLong = "The article is too long to preview or save. Shorten it; the preview shows the last version that fitted.";
    public const string PreviewTooComplex = "The article has too many elements to preview. Split it into several articles or shorten the table or list; the preview shows the last version that fitted.";
    public const string PreviewFailed = "The preview is not available right now. Your text is kept; the preview updates when you type again.";

    public const string ImageButton = "Add image";
    public const string ImageUploading = "Uploading the image\u2026";
    public const string ImageTypeNotAllowed = "Use a PNG, JPEG, GIF or WebP picture.";
    public const string ImageRefused = "The API did not accept this picture.";
    public const string ImageUncertain = "The upload may not have finished. Nothing was added to the article; pick the picture again.";
    public const string ImageFailed = "Couldn't upload the picture. Nothing was added to the article.";

    public static string ImageTooLarge(string limit) => $"This picture is larger than {limit}.";

    public static string Created(string title) => $"Created the draft {title}";

    public static string Saved(string title) => $"Saved {title}";

    public static string Published(string title) => $"Published {title}";

    public static string Archived(string title) => $"Archived {title}";

    public static string PublishNeeds(string field) => field switch
    {
        "title" => "Add a title before you publish.",
        "slug" => "Add a slug before you publish.",
        "body" => "Write the article before you publish.",
        _ => "Choose a category before you publish.",
    };
}
