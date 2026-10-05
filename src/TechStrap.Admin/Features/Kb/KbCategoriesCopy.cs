namespace TechStrap.Admin.Features.Kb;

/// <summary>The words of the categories page: create, rename and re-order, and the delete confirmation that is blocked while articles are in a category. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class KbCategoriesCopy
{
    public const string Heading = "Categories";
    public const string BackToList = "Knowledge base";
    public const string Loading = "Loading categories";
    public const string LoadFailed = "Couldn't load the categories.";
    public const string NoCategories = "No categories yet";
    public const string NoCategoriesHint = "Create a category, then articles can be published in it.";
    public const string TableLabel = "Categories";

    public const string NewHeading = "New category";
    public const string ProductLabel = "Product";
    public const string SharedOption = "Shared by every product";
    public const string ProductHelp = "Chosen once. A shared category can be used by every product's articles; a product's own category only by that product's.";
    public const string NameLabel = "Name";
    public const string SlugLabel = "Slug";
    public const string SlugHelp = "Letters, numbers and hyphens. It is part of the address of every article in the category and can't be changed once the category exists.";
    public const string DescriptionLabel = "Description";
    public const string DescriptionHelp = "Optional. Shown on the portal.";
    public const string SortOrderLabel = "Sort order";
    public const string SortOrderHelp = "Lower numbers come first.";
    public const string Create = "Create category";
    public const string Creating = "Creating\u2026";

    public const string ColumnName = "Category";
    public const string ColumnScope = "Product";
    public const string ColumnSlug = "Slug";
    public const string ColumnOrder = "Order";
    public const string ColumnActions = "Actions";
    public const string Shared = "Shared";
    public const string UnknownProduct = "Another product";
    public const string Edit = "Edit";
    public const string Save = "Save";
    public const string Cancel = "Cancel";
    public const string Delete = "Delete";
    public const string Reload = "Reload list";

    public const string NameRequired = "Enter a name.";
    public const string NameTooLong = "Use 100 characters or fewer.";
    public const string SlugRequired = "Enter a slug.";
    public const string SlugInvalid = "Use lower-case letters, numbers and single hyphens, up to 80 characters.";
    public const string SlugReserved = "The slug \"search\" is kept for the portal's search page. Pick another.";
    public const string SlugTaken = "Another category already uses this slug. Slugs are shared across products, so pick another.";
    public const string DescriptionTooLong = "Use 300 characters or fewer.";
    public const string SortOrderInvalid = "Enter a whole number.";

    public const string DeleteConfirm = "Delete category";
    public const string DeleteBody = "No articles are in this category as far as the list shows. This can't be undone.";
    public const string DeleteUncertain = "The delete may have gone through. Reload the list to check before you try again.";
    public const string CategoryGone = "That category no longer exists.";
    public const string SaveUncertain = "The change may have gone through. Reload the list to check before you try again.";
    public const string SaveConflict = "This category changed since you opened the list. Reload the list, then make your change again.";
    public const string DeleteForbidden = "Only an admin can delete a category.";

    public static string DeleteTitle(string name) => $"Delete the category {name}?";

    public static string DeleteInUse(string reason) => $"This category still has articles, so it can't be deleted. Move them to another category first. {reason}";

    public static string DeleteFailed(string reason) => $"Couldn't delete the category. Nothing was changed. {reason}";

    public static string Created(string name) => $"Created the category {name}";

    public static string Saved(string name) => $"Saved the category {name}";

    public static string Deleted(string name) => $"Deleted the category {name}";
}
