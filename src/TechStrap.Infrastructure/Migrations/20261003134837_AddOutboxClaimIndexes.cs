using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStrap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxClaimIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_email_outbox_status",
                table: "email_outbox");

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_created_at_when_dead_lettered",
                table: "email_outbox",
                column: "created_at",
                filter: "status = 'DeadLettered'");

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_locked_until_when_sending",
                table: "email_outbox",
                column: "locked_until",
                filter: "status = 'Sending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_email_outbox_created_at_when_dead_lettered",
                table: "email_outbox");

            migrationBuilder.DropIndex(
                name: "ix_email_outbox_locked_until_when_sending",
                table: "email_outbox");

            migrationBuilder.CreateIndex(
                name: "ix_email_outbox_status",
                table: "email_outbox",
                column: "status");
        }
    }
}
