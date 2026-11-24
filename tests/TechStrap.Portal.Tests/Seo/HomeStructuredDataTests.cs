using System.Text.Encodings.Web;
using System.Text.Json;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Products;
using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>The structured data of the product home and the help-center home: the shape search engines read, and a hostile product name that stays inside its string.</summary>
public sealed class HomeStructuredDataTests
{
    private static readonly JsonSerializerOptions PackageOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Absolute(string path) => "https://portal.test" + path;

    private static JsonElement Json(object value) => JsonDocument.Parse(JsonSerializer.Serialize(value, value.GetType(), PackageOptions)).RootElement;

    [Fact]
    public void The_product_home_is_a_website_with_a_search_action_on_the_q_parameter()
    {
        var theme = new ProductThemeViewModel("paperplane", "Paperplane", null, null);

        var data = KbStructuredData.ForProductHome(Absolute, "/p/paperplane", "/p/paperplane/kb/search", theme);

        var site = Json(data.ShouldHaveSingleItem());
        site.GetProperty("@type").GetString().ShouldBe("WebSite");
        site.GetProperty("url").GetString().ShouldBe("https://portal.test/p/paperplane");
        var action = site.GetProperty("potentialAction");
        action.GetProperty("@type").GetString().ShouldBe("SearchAction");
        action.GetProperty("target").GetString().ShouldBe("https://portal.test/p/paperplane/kb/search?q={search_term_string}");
        action.GetProperty("query-input").GetString().ShouldBe("required name=search_term_string");
    }

    [Fact]
    public void The_help_center_home_is_a_breadcrumb_trail_and_a_collection_page()
    {
        var theme = new ProductThemeViewModel("paperplane", "Paperplane", null, null);
        KbCrumb[] trail = [new("Paperplane", "/p/paperplane"), new("Help center")];

        var data = KbStructuredData.ForKbHome(Absolute, "/p/paperplane/kb", "Paperplane Help Center", "A description.", trail);

        data.Count.ShouldBe(2);
        var crumbs = Json(data[0]);
        crumbs.GetProperty("@type").GetString().ShouldBe("BreadcrumbList");
        crumbs.GetProperty("itemListElement")[1].GetProperty("item").GetString().ShouldBe("https://portal.test/p/paperplane/kb");
        var page = Json(data[1]);
        page.GetProperty("@type").GetString().ShouldBe("CollectionPage");
        page.GetProperty("url").GetString().ShouldBe("https://portal.test/p/paperplane/kb");
        page.GetProperty("description").GetString().ShouldBe("A description.");
    }

    [Fact]
    public void A_hostile_product_name_cannot_end_the_script_block()
    {
        const string Name = "</script><script>alert(1)</script>";
        var theme = new ProductThemeViewModel("paperplane", Name, null, null);

        var data = KbStructuredData.ForProductHome(Absolute, "/p/paperplane", "/p/paperplane/kb/search", theme);

        var text = JsonSerializer.Serialize(data[0], data[0].GetType(), PackageOptions);
        text.ShouldNotContain("</script>");
        JsonDocument.Parse(text).RootElement.GetProperty("name").GetString().ShouldBe(Name);
    }
}
