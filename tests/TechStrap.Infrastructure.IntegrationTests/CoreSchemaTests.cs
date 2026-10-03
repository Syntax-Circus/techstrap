using Microsoft.EntityFrameworkCore;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The core tables: products, API keys, agents, notification preferences, requesters and tags.</summary>
public sealed class CoreSchemaTests
{
    [Theory]
    [InlineData("products")]
    [InlineData("product_ticket_sequences")]
    [InlineData("product_api_keys")]
    [InlineData("agents")]
    [InlineData("agent_notification_preferences")]
    [InlineData("requesters")]
    [InlineData("tags")]
    public void The_core_tables_are_in_the_model(string table)
    {
        ModelInspector.HasTable(table).ShouldBeTrue();
    }

    [Theory]
    [InlineData("products", "key")]
    [InlineData("products", "number_prefix")]
    [InlineData("product_api_keys", "key_hash")]
    [InlineData("agents", "oidc_subject")]
    [InlineData("requesters", "email")]
    [InlineData("tags", "slug")]
    public void The_documented_unique_indexes_exist(string table, string columns)
    {
        ModelInspector.HasUniqueIndex(ModelInspector.Table(table), columns.Split(',')).ShouldBeTrue();
    }

    [Fact]
    public void Product_rows_carry_the_xmin_concurrency_token()
    {
        var token = ModelInspector.Table("products").GetProperties().Single(p => p.IsConcurrencyToken);

        token.GetColumnName().ShouldBe("xmin");
        token.GetColumnType().ShouldBe("xid");
        token.ClrType.ShouldBe(typeof(uint));
    }

    [Fact]
    public void Requester_email_is_a_case_insensitive_citext_column()
    {
        ModelInspector.Table("requesters").FindProperty("Email")!.GetColumnType().ShouldBe("citext");
    }

    [Fact]
    public void The_agent_public_display_name_is_a_nullable_column_of_at_most_sixty_characters()
    {
        var property = ModelInspector.Table("agents").FindProperty("PublicDisplayName")!;

        property.GetColumnName().ShouldBe("public_display_name");
        property.IsNullable.ShouldBeTrue();
        property.GetMaxLength().ShouldBe(60);
    }
}
