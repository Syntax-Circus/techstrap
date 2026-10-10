using System.Text.Json;
using TechStrap.Contracts.Products;

namespace TechStrap.Application.Tests.Skins;

/// <summary>
/// Skin strictness belongs to the Api request reader only. A response that carries a token added by a later release must still deserialize with the plain
/// client options, or an older Portal, Admin or SDK would fail on the whole product list (D-053).
/// </summary>
public sealed class ProductSkinReadLeniencyTests
{
    [Fact]
    public void An_unknown_member_inside_skin_in_a_response_is_ignored_by_a_plain_reader()
    {
        const string json = "{\"key\":\"orbitly\",\"displayName\":\"Orbitly\",\"accentColour\":\"#1F6FEB\",\"onAccentColour\":\"#FFFFFF\",\"accentInkColour\":\"#1F6FEB\",\"skin\":{\"pack\":\"slate\",\"shine\":\"yes\"}}";

        var dto = JsonSerializer.Deserialize<PublicProductDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        dto!.Skin!.Pack.ShouldBe("slate");
    }
}
