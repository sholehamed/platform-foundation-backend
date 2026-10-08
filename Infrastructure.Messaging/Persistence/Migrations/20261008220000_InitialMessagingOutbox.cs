using Infrastructure.Messaging.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Messaging.Persistence.Migrations;

[DbContext(typeof(PlatformMessagingDbContext))]
[Migration("20261008220000_InitialMessagingOutbox")]
public partial class InitialMessagingOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MessagingOutboxMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Contract = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MessagingOutboxMessages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MessagingOutboxDeliveries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HandlerKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Attempts = table.Column<int>(type: "int", nullable: false),
                NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LeaseExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastErrorType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MessagingOutboxDeliveries", x => x.Id);
                table.ForeignKey(
                    name: "FK_MessagingOutboxDeliveries_MessagingOutboxMessages_MessageId",
                    column: x => x.MessageId,
                    principalTable: "MessagingOutboxMessages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MessagingOutboxMessages_CreatedAt",
            table: "MessagingOutboxMessages",
            column: "CreatedAt");
        migrationBuilder.CreateIndex(
            name: "IX_MessagingOutboxDeliveries_MessageId_HandlerKey",
            table: "MessagingOutboxDeliveries",
            columns: new[] { "MessageId", "HandlerKey" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_MessagingOutboxDeliveries_Status_NextAttemptAt",
            table: "MessagingOutboxDeliveries",
            columns: new[] { "Status", "NextAttemptAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MessagingOutboxDeliveries");
        migrationBuilder.DropTable(name: "MessagingOutboxMessages");
    }
}
