using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Infrastructure.Messaging.Configuration;
using Infrastructure.Messaging.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.Messaging.Interceptors;

/// <summary>
/// Opt-in EF SaveChanges hook. Runs in-memory before-commit handlers and maps
/// domain events to durable messages in the same DbContext write transaction.
/// NEVER publishes INotification or performs network I/O after SaveChanges.
/// </summary>
public sealed class TransactionalDomainEventsInterceptor<TDbContext>(
    IServiceProvider services,
    DomainEventRegistry registry,
    IOutboxMessageStager stager)
    : SaveChangesInterceptor where TDbContext : DbContext
{
    private readonly HashSet<IDomainEvent> pendingEvents =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Guid> stagedMessageIds = [];
    private DbContext? trackedContext;
    private bool dispatching;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } db && HasUnprocessedEvents(db))
            throw new InvalidOperationException(
                "Domain events require SaveChangesAsync to ensure safe asynchronous handlers.");
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var db = eventData.Context;
        if (db is null) return result;
        if (dispatching)
            throw new InvalidOperationException(
                "Nested SaveChangesAsync inside a domain event handler is not supported.");
        if (trackedContext is not null && trackedContext != db)
            throw new InvalidOperationException(
                "Domain event interceptor scope cannot be shared across DbContexts.");
        trackedContext = db;
        dispatching = true;

        try
        {
            // Handlers may raise additional events. Bound the iteration to prevent recursion.
            for (var n = 0; n < 128; n++)
            {
                var events = db.ChangeTracker.Entries<IHasDomainEvents>()
                    .SelectMany(x => x.Entity.DomainEvents)
                    .Where(x => !pendingEvents.Contains(x))
                    .Distinct(ReferenceEqualityComparer.Instance)
                    .ToArray();
                if (events.Length == 0) return result;

                foreach (var evt in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Avoid dispatching an event a second time on a later iteration.
                    pendingEvents.Add(evt);
                    var messages = await registry.DispatchAsync(services, evt, cancellationToken);
                    foreach (var message in messages)
                        stagedMessageIds.Add(stager.Stage(message));
                }
            }

            throw new InvalidOperationException(
                "Domain event dispatch exceeded 128 waves; possible event recursion.");
        }
        catch
        {
            CleanupPending(db);
            throw;
        }
        finally
        {
            dispatching = false;
        }
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        CommitEvents(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        CommitEvents(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
        CleanupPending(eventData.Context);

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        CleanupPending(eventData.Context);
        return Task.CompletedTask;
    }

    private bool HasUnprocessedEvents(DbContext db) =>
        dispatching || db.ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(x => x.Entity.DomainEvents)
            .Any(x => !pendingEvents.Contains(x));

    private void CommitEvents(DbContext? db)
    {
        if (db is null) return;
        foreach (var aggregate in db.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(x => x.Entity))
        {
            // Only events processed in this save are cleared. All pre-existing
            // raised events are processed, including those raised by handlers.
            if (aggregate.DomainEvents.Any(x => pendingEvents.Contains(x)))
                aggregate.ClearDomainEvents();
        }
        pendingEvents.Clear();
        stagedMessageIds.Clear();
        trackedContext = null;
    }

    private void CleanupPending(DbContext? db)
    {
        if (db is not null && stagedMessageIds.Count > 0)
        {
            foreach (var entry in db.ChangeTracker.Entries<OutboxDelivery>()
                .Where(x => stagedMessageIds.Contains(x.Entity.MessageId) &&
                            x.State == EntityState.Added).ToArray())
                entry.State = EntityState.Detached;
            foreach (var entry in db.ChangeTracker.Entries<OutboxMessage>()
                .Where(x => stagedMessageIds.Contains(x.Entity.Id) &&
                            x.State == EntityState.Added).ToArray())
                entry.State = EntityState.Detached;
        }
        // On rollback or SaveChanges failure the aggregate keeps domain events
        // so the caller may fix the transaction and retry safely.
        pendingEvents.Clear();
        stagedMessageIds.Clear();
        trackedContext = null;
    }
}
