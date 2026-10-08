using Microsoft.Extensions.DependencyInjection;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();

        var contributors = scope.ServiceProvider.GetServices<ISeedContributor>();

        foreach (var contributor in contributors.OrderBy(x => x.Order))
        {
            await contributor.SeedAsync(scope.ServiceProvider);
        }
    }
}

public interface ISeedContributor
{
    int Order { get; }
    Task SeedAsync(IServiceProvider serviceProvider);
}
