using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransactionService.Migrations
{
    /// <inheritdoc />
    public partial class InitialTransactionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_inbox_messages",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessAttempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_inbox_messages", x => new { x.ConsumerName, x.EventId });
                });

            migrationBuilder.CreateTable(
                name: "integration_outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EnvelopeJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                columns: table => new
                {
                    TransferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SenderUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SenderAccountId = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    RecipientUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RecipientAccountId = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfers", x => x.TransferId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_inbox_messages_ConsumerName_ProcessedAt",
                table: "integration_inbox_messages",
                columns: new[] { "ConsumerName", "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_PublishedAt_OccurredAt",
                table: "integration_outbox_messages",
                columns: new[] { "PublishedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transfers_IdempotencyKey",
                table: "transfers",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_inbox_messages");

            migrationBuilder.DropTable(
                name: "integration_outbox_messages");

            migrationBuilder.DropTable(
                name: "transfers");
        }
    }
}
