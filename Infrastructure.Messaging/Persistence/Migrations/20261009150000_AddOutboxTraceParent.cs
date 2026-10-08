using Infrastructure.Messaging.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Messaging.Persistence.Migrations;

[DbContext(typeof(PlatformMessagingDbContext))]
[Migration("20261009150000_AddOutboxTraceParent")]
public partial class AddOutboxTraceParent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "TraceParent",
            table: "MessagingOutboxMessages",
            type: "nvarchar(55)",
            maxLength: 55,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("TraceParent", "MessagingOutboxMessages");
}
