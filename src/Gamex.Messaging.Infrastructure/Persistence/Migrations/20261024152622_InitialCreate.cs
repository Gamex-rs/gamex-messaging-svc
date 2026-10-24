using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamex.Messaging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    listing_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    listing_game_title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    initiator_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    initiator_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    initiator_avatar_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    recipient_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    recipient_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    recipient_avatar_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_message_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    conversation_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    sender_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    read_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_initiator_id_last_message_at",
                table: "conversations",
                columns: new[] { "initiator_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_recipient_id_last_message_at",
                table: "conversations",
                columns: new[] { "recipient_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ux_conversations_listing_id_initiator_id",
                table: "conversations",
                columns: new[] { "listing_id", "initiator_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_created_at",
                table: "messages",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_read_at",
                table: "messages",
                columns: new[] { "conversation_id", "read_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "conversations");
        }
    }
}
