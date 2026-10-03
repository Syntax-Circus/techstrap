using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace TechStrap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchVectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "tickets",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(subject, '')), 'A')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "messages",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(body, '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "kb_articles",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', coalesce(title, '')), 'A') || setweight(to_tsvector('english', coalesce(summary, '')), 'B') || setweight(to_tsvector('english', coalesce(body_markdown, '')), 'C')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_search_vector",
                table: "tickets",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_messages_search_vector",
                table: "messages",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_kb_articles_search_vector",
                table: "kb_articles",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tickets_search_vector",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_messages_search_vector",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "ix_kb_articles_search_vector",
                table: "kb_articles");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "kb_articles");
        }
    }
}
