using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStrap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "feature_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Support");

            migrationBuilder.CreateTable(
                name: "features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description_markdown = table.Column<string>(type: "text", nullable: false),
                    publication_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    progress_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_features_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_feature_id",
                table: "tickets",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_type_last_activity_at",
                table: "tickets",
                columns: new[] { "type", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_features_product_id_publication_state_updated_at",
                table: "features",
                columns: new[] { "product_id", "publication_state", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_features_product_id_slug",
                table: "features",
                columns: new[] { "product_id", "slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_features_feature_id",
                table: "tickets",
                column: "feature_id",
                principalTable: "features",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tickets_features_feature_id",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "features");

            migrationBuilder.DropIndex(
                name: "ix_tickets_feature_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_type_last_activity_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "feature_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "type",
                table: "tickets");
        }
    }
}
