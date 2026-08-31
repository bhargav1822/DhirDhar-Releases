using DhirDhar.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DhirDhar.Infrastructure.Persistence.Configurations;

public sealed class UserTextTranslationConfiguration : IEntityTypeConfiguration<UserTextTranslation>
{
    public void Configure(EntityTypeBuilder<UserTextTranslation> builder)
    {
        builder.ToTable("UserTextTranslations");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.SourceText)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(t => t.SourceLanguage)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.TargetLanguage)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.TranslatedText)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired();

        builder.HasIndex(nameof(UserTextTranslation.SourceText), nameof(UserTextTranslation.TargetLanguage))
            .IsUnique();

        builder.HasIndex(t => t.TranslatedText);
    }
}
