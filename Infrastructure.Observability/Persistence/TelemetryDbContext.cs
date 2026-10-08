using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Observability.Persistence;

/// <summary>
/// Standalone telemetry storage; does NOT participate in business transactions.
/// Migrations must be applied explicitly before enabling this persistence adapter.
/// </summary>
public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options)
    : DbContext(options)
{
    public DbSet<TelemetryEntry> Entries => Set<TelemetryEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var entity = builder.Entity<TelemetryEntry>();
        entity.ToTable("PlatformTelemetryEntries");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Kind).HasConversion<int>();
        entity.Property(x => x.Source).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Level).HasMaxLength(16);
        entity.Property(x => x.TraceId).HasMaxLength(32);
        entity.Property(x => x.SpanId).HasMaxLength(16);
        entity.Property(x => x.ParentSpanId).HasMaxLength(16);
        entity.Property(x => x.ErrorType).HasMaxLength(128);
        entity.HasIndex(x => x.TimestampUtc);
        entity.HasIndex(x => new { x.Kind, x.TimestampUtc });
        entity.HasIndex(x => new { x.TraceId, x.TimestampUtc });
        entity.HasIndex(x => new { x.Failed, x.TimestampUtc });
    }
}
