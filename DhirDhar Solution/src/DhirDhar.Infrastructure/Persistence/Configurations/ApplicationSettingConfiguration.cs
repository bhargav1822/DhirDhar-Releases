using DhirDhar.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DhirDhar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures persistence mapping for <see cref="ApplicationSetting"/>. The key is the
/// primary key; the timestamp is stored as UTC in SQLite.
/// </summary>
public sealed class ApplicationSettingConfiguration : IEntityTypeConfiguration<ApplicationSetting>
{
    public void Configure(EntityTypeBuilder<ApplicationSetting> builder)
    {
        builder.ToTable("ApplicationSettings");

        builder.HasKey(setting => setting.Key);

        builder.Property(setting => setting.Key)
            .HasMaxLength(ApplicationSetting.MaxKeyLength)
            .IsRequired();

        builder.Property(setting => setting.Value)
            .HasMaxLength(ApplicationSetting.MaxValueLength)
            .IsRequired();

        builder.Property(setting => setting.UpdatedAt)
            .HasConversion<DateTime?>();
    }
}
