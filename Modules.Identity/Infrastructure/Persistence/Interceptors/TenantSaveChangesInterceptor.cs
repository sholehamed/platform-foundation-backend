using Application.SharedKernel.Exceptions;
using Domain.SharedKernel.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Infrastructure.Persistence.Interceptors
{
    public sealed class TenantSaveChangesInterceptor(ITenantContext tenant) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is null || tenant.IsFilterDisabled)
                return base.SavingChangesAsync(eventData, result, cancellationToken);

            var writable = tenant.WritableTenantIds;

            foreach (var entry in eventData.Context.ChangeTracker.Entries<IMustHaveTenant>())
            {
                if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;

                if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                    entry.Entity.TenantId = tenant.ActiveTenantId
                        ?? throw new InvalidOperationException("Active tenant not resolved.");

                //if (!writable.Contains(entry.Entity.TenantId))
                //    throw new ServiceExeption(
                //        $"Write denied for tenant {entry.Entity.TenantId}.");

                //if (entry.State == EntityState.Modified)
                //{
                //    var original = entry.Property(nameof(IMustHaveTenant.TenantId)).OriginalValue;
                //    if (!Equals(original, entry.Entity.TenantId))
                //        throw new ServiceExeption("Tenant reassignment not allowed.");
                //}
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

}
