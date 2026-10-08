using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Observability.Persistence;

public sealed class TelemetryDesignTimeFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("PLATFORM_TELEMETRY_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "PLATFORM_TELEMETRY_CONNECTION_STRING is required for migrations.");
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlServer(connection).Options;
        return new TelemetryDbContext(options);
    }
}
