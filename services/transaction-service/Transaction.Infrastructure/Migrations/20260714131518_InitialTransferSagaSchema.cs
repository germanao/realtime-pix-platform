using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transaction.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTransferSagaSchema : Migration
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
                name: "saga_transitions",
                columns: table => new
                {
                    TransitionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    NextState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PreviousVersion = table.Column<int>(type: "integer", nullable: false),
                    NextVersion = table.Column<int>(type: "integer", nullable: false),
                    TriggeringMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TriggeringMessageType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CausationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saga_transitions", x => x.TransitionId);
                });

            migrationBuilder.CreateTable(
                name: "transfer_sagas",
                columns: table => new
                {
                    TransferId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SenderUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SenderAccountId = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    SenderBankId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecipientUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RecipientAccountId = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    RecipientBankId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SimulationMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompensationStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompensatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_sagas", x => x.TransferId);
                    table.CheckConstraint("CK_transfer_sagas_positive_amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_transfer_sagas_positive_version", "\"Version\" > 0");
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
                name: "IX_integration_outbox_messages_Status_ClaimedUntil_OccurredAt",
                table: "integration_outbox_messages",
                columns: new[] { "Status", "ClaimedUntil", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_saga_transitions_TransferId_NextVersion",
                table: "saga_transitions",
                columns: new[] { "TransferId", "NextVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_saga_transitions_TransferId_RecordedAt",
                table: "saga_transitions",
                columns: new[] { "TransferId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_sagas_IdempotencyKey",
                table: "transfer_sagas",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfer_sagas_State_DeadlineAt",
                table: "transfer_sagas",
                columns: new[] { "State", "DeadlineAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_inbox_messages");

            migrationBuilder.DropTable(
                name: "integration_outbox_messages");

            migrationBuilder.DropTable(
                name: "saga_transitions");

            migrationBuilder.DropTable(
                name: "transfer_sagas");
        }
    }
}
