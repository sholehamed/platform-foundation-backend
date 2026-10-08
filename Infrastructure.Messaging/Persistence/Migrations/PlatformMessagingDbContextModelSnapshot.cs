using Infrastructure.Messaging.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Infrastructure.Messaging.Persistence.Migrations;

/// <summary>EF Core snapshot for the optional standalone message store.</summary>
[DbContext(typeof(PlatformMessagingDbContext))]
public sealed class PlatformMessagingDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.10");
        modelBuilder.AddMessagingOutbox();
    }
}
