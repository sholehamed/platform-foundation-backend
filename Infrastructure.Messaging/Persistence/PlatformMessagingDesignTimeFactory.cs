using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Messaging.Persistence;

/// <summary>Allows SQL Server migrations without starting Hangfire or the Web host.</summary>
public sealed class PlatformMessagingDesignTimeFactory
    : IDesignTimeDbContextFactory<PlatformMessagingDbContext>
{
    public PlatformMessagingDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("PLATFORM_MESSAGING_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "Set PLATFORM_MESSAGING_CONNECTION_STRING before running dotnet ef.");

        var options = new DbContextOptionsBuilder<PlatformMessagingDbContext>()
            .UseSqlServer(connection)
            .Options;
        return new PlatformMessagingDbContext(options);
    }
}
