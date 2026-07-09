using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WalletLedgerService.Migrations
{
    /// <inheritdoc />
    public partial class InitialWalletLedgerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    AccountId = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    UserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BankName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.AccountId);
                });

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
                name: "ledger_entries",
                columns: table => new
                {
                    LedgerEntryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountId = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    UserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntryType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TransferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CounterpartyUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.LedgerEntryId);
                });

            migrationBuilder.CreateTable(
                name: "processed_transfers",
                columns: table => new
                {
                    TransferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_transfers", x => x.TransferId);
                });

            migrationBuilder.CreateTable(
                name: "welcome_grants",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_welcome_grants", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_UserId_BankName",
                table: "accounts",
                columns: new[] { "UserId", "BankName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_inbox_messages_ConsumerName_ProcessedAt",
                table: "integration_inbox_messages",
                columns: new[] { "ConsumerName", "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_PublishedAt_OccurredAt",
                table: "integration_outbox_messages",
                columns: new[] { "PublishedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_AccountId_OccurredAt",
                table: "ledger_entries",
                columns: new[] { "AccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_TransferId_EntryType",
                table: "ledger_entries",
                columns: new[] { "TransferId", "EntryType" },
                unique: true,
                filter: "\"TransferId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "integration_inbox_messages");

            migrationBuilder.DropTable(
                name: "integration_outbox_messages");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "processed_transfers");

            migrationBuilder.DropTable(
                name: "welcome_grants");
        }
    }
}
