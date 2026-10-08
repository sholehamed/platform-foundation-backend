using Application.SharedKernel.Observability;
using Infrastructure.Observability.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Infrastructure.Observability.Persistence.Migrations;

/// <summary>Frozen baseline; do not call OnModelCreating of the mutable live context.</summary>
[DbContext(typeof(TelemetryDbContext))]
public sealed class TelemetryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder builder)
    {
        builder.HasAnnotation("ProductVersion", "10.0.10");
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
