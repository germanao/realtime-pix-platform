using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityPresenceService.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityPresenceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anonymous_sessions",
                columns: table => new
                {
                    SessionToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    UserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anonymous_sessions", x => x.SessionToken);
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
                name: "presence_connections",
                columns: table => new
                {
                    ConnectionId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    UserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presence_connections", x => x.ConnectionId);
                });

            migrationBuilder.CreateTable(
                name: "presence_users",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IsBot = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_presence_users", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_anonymous_sessions_UserId",
                table: "anonymous_sessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_inbox_messages_ConsumerName_ProcessedAt",
                table: "integration_inbox_messages",
                columns: new[] { "ConsumerName", "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_messages_PublishedAt_OccurredAt",
                table: "integration_outbox_messages",
                columns: new[] { "PublishedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_presence_connections_UserId",
                table: "presence_connections",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_presence_users_ClientId",
                table: "presence_users",
                column: "ClientId",
                unique: true,
                filter: "\"ClientId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anonymous_sessions");

            migrationBuilder.DropTable(
                name: "integration_inbox_messages");

            migrationBuilder.DropTable(
                name: "integration_outbox_messages");

            migrationBuilder.DropTable(
                name: "presence_connections");

            migrationBuilder.DropTable(
                name: "presence_users");
        }
    }
}
