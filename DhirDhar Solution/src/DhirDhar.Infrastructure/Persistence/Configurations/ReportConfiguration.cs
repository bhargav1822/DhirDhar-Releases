using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DhirDhar.Infrastructure.Persistence.Configurations;

public sealed class ReportConfiguration : IEntityTypeConfiguration<Domain.Entities.Report>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Report> builder)
    {
        builder.ToTable("Reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReportName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ReportType).HasMaxLength(50).IsRequired();
        builder.Property(r => r.GeneratedDate).IsRequired();
        builder.Property(r => r.FilePath).HasMaxLength(500).IsRequired();
    }
}
