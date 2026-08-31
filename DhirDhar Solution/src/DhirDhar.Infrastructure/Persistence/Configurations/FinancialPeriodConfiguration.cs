using DhirDhar.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DhirDhar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures persistence mapping for <see cref="FinancialPeriod"/>.
/// </summary>
public sealed class FinancialPeriodConfiguration : IEntityTypeConfiguration<FinancialPeriod>
{
    public void Configure(EntityTypeBuilder<FinancialPeriod> builder)
    {
        builder.ToTable("FinancialPeriods");

        builder.HasKey(period => period.Id);

        builder.Property(period => period.Name)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(period => period.StartDate)
            .HasConversion<DateTime?>();

        builder.Property(period => period.EndDate)
            .HasConversion<DateTime?>();

        builder.Property(period => period.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(period => period.CreatedAt)
            .HasConversion<DateTime?>();

        builder.Property(period => period.UpdatedAt)
            .HasConversion<DateTime?>();
    }
}
