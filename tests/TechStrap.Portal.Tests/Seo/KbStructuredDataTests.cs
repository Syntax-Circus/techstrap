using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>PHASE-09c T14: the shape of the article's structured data, and Review Focus 1: every string of it, whatever an author wrote, stays inside its string.</summary>
public sealed class KbStructuredDataTests
{
    private static readonly JsonSerializerOptions PackageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static readonly ProductThemeViewModel Theme = new("paperplane", "Paperplane", "#F59E0B", null);
    private static readonly DateTimeOffset Published = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    // What ISeoUrlBuilder.AbsoluteUrl does: a path gets the public address in front, an absolute address stays as it is.
    private static string Absolute(string path) => path.StartsWith("https://", StringComparison.Ordinal) ? path : "https://portal.test" + path;

    private static PublishedKbArticleDto Article(string title = "Reset your password", string category = "Accounts") =>
        new("paperplane", "accounts", category, "reset-password", title, "How to reset it", "<p>Open settings.</p>", Published, Updated);

    private static KbCrumb[] Trail(PublishedKbArticleDto article) =>
    [
        new(Theme.DisplayName, "/p/paperplane"),
        new("Help centre", "/p/paperplane/kb"),
        new(article.CategoryName, "/p/paperplane/kb/accounts"),
        new(article.Title),
    ];

    private static string[] Scripts(PublishedKbArticleDto article, string description = "How to reset it") =>
        [.. KbStructuredData.ForArticle(Absolute, Theme, article, description, Trail(article)).Select(data => JsonSerializer.Serialize(data, data.GetType(), PackageOptions))];

    [Fact]
    public void An_article_has_a_breadcrumb_list_and_an_article_both_schema_org()
    {
        var article = Article();

        var scripts = Scripts(article);

        scripts.Length.ShouldBe(2);
        using var breadcrumbs = JsonDocument.Parse(scripts[0]);
        var list = breadcrumbs.RootElement;
        list.GetProperty("@context").GetString().ShouldBe("https://schema.org");
        list.GetProperty("@type").GetString().ShouldBe("BreadcrumbList");
        var items = list.GetProperty("itemListElement").EnumerateArray().ToList();
        items.Select(item => item.GetProperty("position").GetInt32()).ShouldBe([1, 2, 3, 4]);
        items.Select(item => item.GetProperty("@type").GetString()).ShouldAllBe(type => type == "ListItem");
        items.Select(item => item.GetProperty("name").GetString()).ShouldBe(["Paperplane", "Help centre", "Accounts", "Reset your password"]);
        items.Select(item => item.GetProperty("item").GetString()).ShouldBe(
        [
            "https://portal.test/p/paperplane",
            "https://portal.test/p/paperplane/kb",
            "https://portal.test/p/paperplane/kb/accounts",
            "https://portal.test/p/paperplane/kb/accounts/reset-password",
        ]);

        using var page = JsonDocument.Parse(scripts[1]);
        var root = page.RootElement;
        root.GetProperty("@context").GetString().ShouldBe("https://schema.org");
        root.GetProperty("@type").GetString().ShouldBe("Article");
        root.GetProperty("headline").GetString().ShouldBe("Reset your password");
        root.GetProperty("description").GetString().ShouldBe("How to reset it");
        root.GetProperty("datePublished").GetDateTimeOffset().ShouldBe(Published);
        root.GetProperty("dateModified").GetDateTimeOffset().ShouldBe(Updated);
        root.GetProperty("mainEntityOfPage").GetProperty("@type").GetString().ShouldBe("WebPage");
        root.GetProperty("mainEntityOfPage").GetProperty("@id").GetString().ShouldBe("https://portal.test/p/paperplane/kb/accounts/reset-password");
        root.GetProperty("image").GetString().ShouldBe("https://portal.test/icon-512.png");
        root.GetProperty("author").GetProperty("@type").GetString().ShouldBe("Organization");
        root.GetProperty("author").GetProperty("name").GetString().ShouldBe("Paperplane");
        root.GetProperty("publisher").GetProperty("name").GetString().ShouldBe("Paperplane");
    }

    [Fact]
    public void The_image_is_the_products_logo_when_it_has_one()
    {
        var theme = Theme with { LogoUrl = "https://cdn.example.com/paperplane.png" };
        var article = Article();

        var data = KbStructuredData.ForArticle(Absolute, theme, article, "d", Trail(article));

        using var page = JsonDocument.Parse(JsonSerializer.Serialize(data[1], data[1].GetType(), PackageOptions));
        page.RootElement.GetProperty("image").GetString().ShouldBe("https://cdn.example.com/paperplane.png");
    }

    [Theory]
    [InlineData("</script><img src=x onerror=alert(1)>")]
    [InlineData("<!-- x --> & <b>y</b> '+'")]
    [InlineData("</SCRIPT >")]
    public void A_hostile_title_category_or_description_cannot_leave_its_string_and_reads_back_unchanged(string hostile)
    {
        var article = Article(hostile, hostile);

        var scripts = Scripts(article, hostile);

        foreach (var script in scripts)
        {
            script.ShouldNotContain("<");
            script.ShouldNotContain(">");
            script.ShouldNotContain("&");
        }

        using var breadcrumbs = JsonDocument.Parse(scripts[0]);
        var names = breadcrumbs.RootElement.GetProperty("itemListElement").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToList();
        names[2].ShouldBe(hostile);
        names[3].ShouldBe(hostile);
        using var page = JsonDocument.Parse(scripts[1]);
        page.RootElement.GetProperty("headline").GetString().ShouldBe(hostile);
        page.RootElement.GetProperty("description").GetString().ShouldBe(hostile);
    }

    [Fact]
    public void A_hostile_product_name_cannot_leave_its_string_either()
    {
        var theme = Theme with { DisplayName = "</script><i>Acme</i>" };
        var article = Article();

        var data = KbStructuredData.ForArticle(Absolute, theme, article, "d", Trail(article));
        var scripts = data.Select(item => JsonSerializer.Serialize(item, item.GetType(), PackageOptions)).ToList();

        scripts.ShouldAllBe(script => !script.Contains('<') && !script.Contains('>'));
        using var page = JsonDocument.Parse(scripts[1]);
        page.RootElement.GetProperty("publisher").GetProperty("name").GetString().ShouldBe("</script><i>Acme</i>");
    }

    [Fact]
    public void The_last_step_of_the_trail_is_the_page_itself_and_a_trail_without_links_still_has_addresses()
    {
        var article = Article();

        var data = KbStructuredData.ForArticle(Absolute, Theme, article, "d", [new KbCrumb("Only")]);

        using var breadcrumbs = JsonDocument.Parse(JsonSerializer.Serialize(data[0], data[0].GetType(), PackageOptions));
        breadcrumbs.RootElement.GetProperty("itemListElement")[0].GetProperty("item").GetString().ShouldBe("https://portal.test/p/paperplane/kb/accounts/reset-password");
    }

    [Fact]
    public void A_missing_argument_is_refused()
    {
        var article = Article();

        Should.Throw<ArgumentNullException>(() => KbStructuredData.ForArticle(null!, Theme, article, "d", []));
        Should.Throw<ArgumentNullException>(() => KbStructuredData.ForArticle(Absolute, null!, article, "d", []));
        Should.Throw<ArgumentNullException>(() => KbStructuredData.ForArticle(Absolute, Theme, null!, "d", []));
        Should.Throw<ArgumentNullException>(() => KbStructuredData.ForArticle(Absolute, Theme, article, "d", null!));
    }

    private const string HostileLogo = "https://cdn.example.com/a</script><img/src=x/onerror=alert(1)>.png";

    [Fact]
    public void A_hostile_logo_address_cannot_leave_its_string_and_reads_back_unchanged()
    {
        var theme = Theme with { LogoUrl = HostileLogo };
        var article = Article();

        var data = KbStructuredData.ForArticle(Absolute, theme, article, "d", Trail(article));
        var scripts = data.Select(item => JsonSerializer.Serialize(item, item.GetType(), PackageOptions)).ToList();

        scripts.ShouldAllBe(script => !script.Contains('<') && !script.Contains('>') && !script.Contains('&'));
        using var page = JsonDocument.Parse(scripts[1]);
        page.RootElement.GetProperty("image").GetString().ShouldBe(HostileLogo);
    }

    [Fact]
    public void Every_string_of_every_structured_data_record_is_a_JsonLdText_except_the_context_and_the_type()
    {
        var records = new[] { typeof(BreadcrumbItemLd), typeof(BreadcrumbListLd), typeof(WebPageLd), typeof(OrganizationLd), typeof(ArticleSchema) };

        var plain = records
            .SelectMany(record => record.GetProperties(BindingFlags.Public | BindingFlags.Instance), (record, property) => (record, property))
            .Where(pair => pair.property.PropertyType == typeof(string))
            .Where(pair => (pair.property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? pair.property.Name) is not ("@type" or "@context"))
            .Select(pair => pair.record.Name + "." + pair.property.Name)
            .ToList();

        plain.ShouldBeEmpty("a plain string is written without escaping <, so it must be a JsonLdText");
    }
}
