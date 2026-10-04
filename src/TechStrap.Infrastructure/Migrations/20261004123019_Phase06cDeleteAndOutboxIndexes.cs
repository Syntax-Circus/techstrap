using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStrap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase06cDeleteAndOutboxIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tickets_tickets_parent_ticket_id",
                table: "tickets");

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_kind_to_address_created_at",
                table: "email_outbox",
                columns: new[] { "kind", "to_address", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_tickets_parent_ticket_id",
                table: "tickets",
                column: "parent_ticket_id",
                principalTable: "tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tickets_tickets_parent_ticket_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_email_outbox_kind_to_address_created_at",
                table: "email_outbox");

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_tickets_parent_ticket_id",
                table: "tickets",
                column: "parent_ticket_id",
                principalTable: "tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
