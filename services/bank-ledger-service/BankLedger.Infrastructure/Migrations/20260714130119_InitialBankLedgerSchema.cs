using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankLedger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBankLedgerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_accounts",
                columns: table => new
                {
                    AccountId = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    UserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BankId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BankName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_accounts", x => x.AccountId);
                    table.CheckConstraint("CK_bank_accounts_nonnegative_balance", "\"Balance\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "bank_ledger_entries",
                columns: table => new
                {
                    LedgerEntryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountId = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    UserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BankId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TransferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CounterpartyUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_ledger_entries", x => x.LedgerEntryId);
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
                    MessageKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    DestinationKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Destination = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    EnvelopeJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishAttempts = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ClaimedBy = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "processed_bank_operations",
                columns: table => new
                {
                    TransferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_bank_operations", x => new { x.TransferId, x.OperationType });
                });

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_UserId_BankId",
                table: "bank_accounts",
                columns: new[] { "UserId", "BankId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_ledger_entries_AccountId_OccurredAt",
                table: "bank_ledger_entries",
                columns: new[] { "AccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_bank_ledger_entries_TransferId_OperationType",
                table: "bank_ledger_entries",
                columns: new[] { "TransferId", "OperationType" },
                unique: true,
                filter: "\"TransferId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_integration_inbox_messages_ConsumerName_ProcessedAt",
                table: "integration_inbox_messages",
                columns: new[] { "ConsumerName", "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_PublishedAt_OccurredAt",
                table: "integration_outbox_messages",
                columns: new[] { "PublishedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_Status_ClaimedUntil_OccurredAt",
                table: "integration_outbox_messages",
                columns: new[] { "Status", "ClaimedUntil", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_accounts");

            migrationBuilder.DropTable(
                name: "bank_ledger_entries");

            migrationBuilder.DropTable(
                name: "integration_inbox_messages");

            migrationBuilder.DropTable(
                name: "integration_outbox_messages");

            migrationBuilder.DropTable(
                name: "processed_bank_operations");
        }
    }
}
