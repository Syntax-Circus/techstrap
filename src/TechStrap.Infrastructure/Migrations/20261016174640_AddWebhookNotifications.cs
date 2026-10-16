using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStrap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    endpoints = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_event_id_endpoint_id",
                table: "webhook_deliveries",
                columns: new[] { "event_id", "endpoint_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_product_id_created_at",
                table: "webhook_deliveries",
                columns: new[] { "product_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_status_locked_until",
                table: "webhook_deliveries",
                columns: new[] { "status", "locked_until" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_status_next_attempt_at",
                table: "webhook_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_ticket_id",
                table: "webhook_deliveries",
                column: "ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_deliveries");

            migrationBuilder.DropTable(
                name: "webhook_settings");
        }
    }
}
