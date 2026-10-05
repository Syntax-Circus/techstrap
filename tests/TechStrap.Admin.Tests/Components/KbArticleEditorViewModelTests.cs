using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The editor's form model checks with the server's limits, builds the two requests and knows what publishing still needs.</summary>
public sealed class KbArticleEditorViewModelTests
{
    private static KbArticleEditorViewModel Complete() => new()
    {
        Id = TestData.ArticleId, ProductId = TestData.OrbitlyId, CategoryId = TestData.AccountCategoryId, Slug = "reset-password", Title = "Reset your password", Summary = "How.", Body = "# Steps", Version = 4,
    };

    [Fact]
    public void A_loaded_article_fills_the_form_and_a_missing_summary_is_an_empty_string()
    {
        var model = KbArticleEditorViewModel.From(TestData.KbArticle(summary: "x") with { Summary = null, Status = KbArticleStatuses.Archived, Version = 9 });

        model.Summary.ShouldBe(string.Empty);
        model.Status.ShouldBe("Archived");
        model.Version.ShouldBe(9u);
        model.Id.ShouldBe(TestData.ArticleId);
    }

    [Fact]
    public void Create_sends_the_trimmed_text_a_blank_summary_as_null_and_the_body_untouched()
    {
        var model = Complete();
        model.Title = "  Reset  ";
        model.Slug = " reset ";
        model.Summary = "   ";
        model.Body = "  # Steps\n";

        var request = model.ToCreateRequest();

        request.ShouldBe(new CreateKbArticleRequest(TestData.OrbitlyId, TestData.AccountCategoryId, "reset", "Reset", null, "  # Steps\n"));
    }

    // Review Focus 4: the version the article was loaded with always travels, and the product and the slug never do.
    [Fact]
    public void Update_carries_the_loaded_version_and_no_product_or_slug()
    {
        var request = Complete().ToUpdateRequest();

        request.ShouldBe(new UpdateKbArticleRequest(TestData.AccountCategoryId, "Reset your password", "How.", "# Steps", 4u));
        typeof(UpdateKbArticleRequest).GetProperties().Select(p => p.Name).ShouldBe(["CategoryId", "Title", "Summary", "BodyMarkdown", "Version"]);
    }

    [Fact]
    public void The_snapshot_is_equal_until_a_field_changes_and_equal_again_when_the_text_is_typed_back()
    {
        var model = Complete();
        var saved = model.Take();

        model.Take().ShouldBe(saved);
        model.Body = "# Steps!";
        model.Take().ShouldNotBe(saved);
        model.Body = "# Steps";
        model.Take().ShouldBe(saved);
        model.CategoryId = null;
        model.Take().ShouldNotBe(saved);
    }

    [Theory]
    [InlineData(ApiFields.Title, "", "Enter a title.")]
    [InlineData(ApiFields.Title, "  ", "Enter a title.")]
    [InlineData(ApiFields.Slug, "", "Enter a slug.")]
    [InlineData(ApiFields.Slug, "Reset Password", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData(ApiFields.Slug, "reset--password", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData(ApiFields.Body, "", "Write the article before you save it.")]
    public void A_field_that_fails_says_why_in_the_servers_terms(string field, string value, string message)
    {
        var model = Complete();
        switch (field)
        {
            case ApiFields.Title:
                model.Title = value;
                break;
            case ApiFields.Slug:
                model.Slug = value;
                break;
            default:
                model.Body = value;
                break;
        }

        model.Check(field, creating: true).ShouldBe(message);
    }

    [Fact]
    public void The_limits_are_the_servers_and_a_slug_is_checked_only_when_the_article_is_created()
    {
        var model = Complete();

        model.Title = new string('a', KbEditorLimits.TitleMaxLength);
        model.Check(ApiFields.Title, true).ShouldBeNull();
        model.Title = new string('a', KbEditorLimits.TitleMaxLength + 1);
        model.Check(ApiFields.Title, true).ShouldBe("Use 200 characters or fewer.");
        model.Summary = new string('a', KbEditorLimits.SummaryMaxLength + 1);
        model.Check(ApiFields.Summary, true).ShouldBe("Use 500 characters or fewer.");
        model.Body = new string('a', KbEditorLimits.BodyMaxLength + 1);
        model.Check(ApiFields.Body, true).ShouldBe("The article is too long to save.");
        model.Slug = "Not A Slug";
        model.Check(ApiFields.Slug, creating: false).ShouldBeNull();
        KbEditorLimits.SlugMaxLength.ShouldBe(80);
        KbEditorLimits.BodyMaxLength.ShouldBe(200_000);
    }

    [Fact]
    public void Publishing_needs_a_title_a_slug_a_body_and_a_category_and_names_the_first_that_is_missing()
    {
        Complete().FirstMissingForPublish().ShouldBeNull();
        var noTitle = Complete();
        noTitle.Title = " ";
        noTitle.FirstMissingForPublish().ShouldBe("title");
        var noBody = Complete();
        noBody.Body = "";
        noBody.FirstMissingForPublish().ShouldBe("body");
        var noCategory = Complete();
        noCategory.CategoryId = null;
        noCategory.FirstMissingForPublish().ShouldBe("category");
    }

    [Theory]
    [InlineData("Reset your password", "reset-your-password")]
    [InlineData("  Hello, World!  ", "hello-world")]
    [InlineData("100% sure -- really", "100-sure-really")]
    [InlineData("!!!", "")]
    public void A_slug_is_suggested_from_the_title(string title, string expected) => KbArticleEditorViewModel.SlugFrom(title).ShouldBe(expected);

    [Fact]
    public void A_suggested_slug_is_cut_to_the_limit_without_a_dangling_hyphen()
    {
        var slug = KbArticleEditorViewModel.SlugFrom(string.Join(' ', Enumerable.Repeat("word", 40)));

        slug.Length.ShouldBeLessThanOrEqualTo(KbEditorLimits.SlugMaxLength);
        slug.ShouldNotEndWith("-");
    }
}

/// <summary>What the editor's choices resolve against.</summary>
public sealed class KbEditorLookupsTests
{
    private static readonly Guid Orbitly = TestData.OrbitlyId;
    private static readonly Guid Paperplane = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static KbEditorLookups Lookups() => new(
        [TestData.Product("Paperplane", Paperplane), TestData.Product("Orbitly", Orbitly)],
        [
            TestData.KbCategory("Billing", "billing", Orbitly, Guid.NewGuid(), sortOrder: 20),
            TestData.KbCategory("Getting started", "getting-started", null, Guid.NewGuid(), sortOrder: 10),
            TestData.KbCategory("Shipping", "shipping", Paperplane, Guid.NewGuid(), sortOrder: 5),
        ]);

    [Fact]
    public void A_product_article_may_use_a_shared_category_or_its_own_and_a_shared_article_only_a_shared_one()
    {
        Lookups().CategoriesFor(Orbitly).Select(c => c.Name).ShouldBe(["Getting started", "Billing"]);
        Lookups().CategoriesFor(Paperplane).Select(c => c.Name).ShouldBe(["Shipping", "Getting started"]);
        Lookups().CategoriesFor(null).Select(c => c.Name).ShouldBe(["Getting started"]);
    }

    [Fact]
    public void A_shared_article_is_linked_under_the_first_product_by_name_and_a_product_article_under_its_own()
    {
        Lookups().PortalProductKey(Paperplane).ShouldBe("paperplane");
        Lookups().PortalProductKey(null).ShouldBe("orbitly");
        Lookups().PortalProductKey(Guid.NewGuid()).ShouldBeNull();
        KbEditorLookups.Empty.PortalProductKey(null).ShouldBeNull();
    }

    [Fact]
    public void Names_and_slugs_resolve_and_an_unknown_id_is_a_fixed_phrase_or_nothing()
    {
        var lookups = Lookups();

        lookups.ProductName(null).ShouldBe("Shared");
        lookups.ProductName(Orbitly).ShouldBe("Orbitly");
        lookups.ProductName(Guid.NewGuid()).ShouldBe("Another product");
        lookups.CategorySlug(lookups.Categories[0].Id).ShouldBe("billing");
        lookups.CategorySlug(Guid.NewGuid()).ShouldBeNull();
        lookups.CategorySlug(null).ShouldBeNull();
    }
}
