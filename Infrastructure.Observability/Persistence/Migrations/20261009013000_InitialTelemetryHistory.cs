using Infrastructure.Observability.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Observability.Persistence.Migrations;

[DbContext(typeof(TelemetryDbContext))]
[Migration("20261009013000_InitialTelemetryHistory")]
public partial class InitialTelemetryHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformTelemetryEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                Source = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                DurationMs = table.Column<double>(type: "float", nullable: true),
                Failed = table.Column<bool>(type: "bit", nullable: false),
                Slow = table.Column<bool>(type: "bit", nullable: false),
                TraceId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                SpanId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                ParentSpanId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                ErrorType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_PlatformTelemetryEntries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_PlatformTelemetryEntries_TimestampUtc",
            table: "PlatformTelemetryEntries", column: "TimestampUtc");
        migrationBuilder.CreateIndex(
            name: "IX_PlatformTelemetryEntries_Kind_TimestampUtc",
            table: "PlatformTelemetryEntries", columns: new[] { "Kind", "TimestampUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_PlatformTelemetryEntries_TraceId_TimestampUtc",
            table: "PlatformTelemetryEntries", columns: new[] { "TraceId", "TimestampUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_PlatformTelemetryEntries_Failed_TimestampUtc",
            table: "PlatformTelemetryEntries", columns: new[] { "Failed", "TimestampUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "PlatformTelemetryEntries");
}
