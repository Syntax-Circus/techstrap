using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStrap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSkinAndSiteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "skin",
                table: "products",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "site_settings",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    default_pack = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_settings", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "site_settings",
                columns: new[] { "id", "default_pack" },
                values: new object[] { (short)1, "classic" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_settings");

            migrationBuilder.DropColumn(
                name: "skin",
                table: "products");
        }
    }
}
