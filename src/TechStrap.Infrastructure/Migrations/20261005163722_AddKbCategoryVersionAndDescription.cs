using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStrap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKbCategoryVersionAndDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "kb_categories",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "kb_categories",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                table: "kb_categories");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "kb_categories");
        }
    }
}
