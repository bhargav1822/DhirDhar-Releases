using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DhirDhar.Infrastructure.Persistence.Configurations;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<Domain.Entities.AuditEntry>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Timestamp).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.Description).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Result).HasMaxLength(20).IsRequired();
        builder.Property(a => a.BeforeValue).HasMaxLength(500);
        builder.Property(a => a.AfterValue).HasMaxLength(500);

        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.Action);
        builder.HasIndex(a => a.EntityType);
    }
}
