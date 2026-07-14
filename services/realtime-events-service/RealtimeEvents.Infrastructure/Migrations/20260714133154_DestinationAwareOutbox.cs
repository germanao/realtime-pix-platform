using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealtimeEventsService.Migrations
{
    /// <inheritdoc />
    public partial class DestinationAwareOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                table: "integration_outbox_messages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedUntil",
                table: "integration_outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Destination",
                table: "integration_outbox_messages",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DestinationKind",
                table: "integration_outbox_messages",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MessageKind",
                table: "integration_outbox_messages",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "integration_outbox_messages",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_Status_ClaimedUntil_OccurredAt",
                table: "integration_outbox_messages",
                columns: new[] { "Status", "ClaimedUntil", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_integration_outbox_messages_Status_ClaimedUntil_OccurredAt",
                table: "integration_outbox_messages");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "integration_outbox_messages");

            migrationBuilder.DropColumn(
                name: "ClaimedUntil",
                table: "integration_outbox_messages");

            migrationBuilder.DropColumn(
                name: "Destination",
                table: "integration_outbox_messages");

            migrationBuilder.DropColumn(
                name: "DestinationKind",
                table: "integration_outbox_messages");

            migrationBuilder.DropColumn(
                name: "MessageKind",
                table: "integration_outbox_messages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "integration_outbox_messages");
        }
    }
}
