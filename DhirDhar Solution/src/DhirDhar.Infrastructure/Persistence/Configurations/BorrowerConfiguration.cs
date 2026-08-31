using DhirDhar.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DhirDhar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures persistence mapping for <see cref="Borrower"/>. The <c>Status</c>
/// enum is stored as an integer; dates are stored as UTC in SQLite.
/// </summary>
public sealed class BorrowerConfiguration : IEntityTypeConfiguration<Borrower>
{
    public void Configure(EntityTypeBuilder<Borrower> builder)
    {
        builder.ToTable("Borrowers");

        builder.HasKey(borrower => borrower.Id);

        builder.Property(borrower => borrower.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(borrower => borrower.FatherName)
            .HasMaxLength(100);

        builder.Property(borrower => borrower.Surname)
            .HasMaxLength(100);

        builder.Property(borrower => borrower.Village)
            .HasMaxLength(100);

        builder.Property(borrower => borrower.Phone)
            .HasMaxLength(30);

        builder.Property(borrower => borrower.AadharNumber)
            .HasMaxLength(12);

        builder.Property(borrower => borrower.Address)
            .HasMaxLength(200);

        builder.Property(borrower => borrower.Notes)
            .HasMaxLength(1000);

        builder.Property(borrower => borrower.BorrowerPhotoPath)
            .HasMaxLength(500);

        builder.Property(borrower => borrower.OrnamentPhotoPath)
            .HasMaxLength(500);

        builder.Property(borrower => borrower.LoanType)
            .HasMaxLength(50);

        builder.Property(borrower => borrower.OrnamentType)
            .HasMaxLength(50);

        builder.Property(borrower => borrower.OrnamentWeight)
            .HasPrecision(18, 2);

        builder.Property(borrower => borrower.LoanAmount)
            .HasPrecision(18, 2);

        builder.Property(borrower => borrower.LoanDate);

        builder.Property(borrower => borrower.InterestRate)
            .HasPrecision(18, 4);

        builder.Property(borrower => borrower.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(borrower => borrower.ClosedDate);

        builder.Property(borrower => borrower.ClosingAmount)
            .HasPrecision(18, 2);

        builder.Property(borrower => borrower.ClosedAccruedInterest)
            .HasPrecision(18, 2);

        builder.Ignore(borrower => borrower.Contact);

        builder.Property(borrower => borrower.CreatedAt)
            .HasConversion<DateTime?>();

        builder.Property(borrower => borrower.UpdatedAt)
            .HasConversion<DateTime?>();

        builder.HasIndex(borrower => borrower.BorrowerNumber)
            .IsUnique();
        builder.HasIndex(borrower => borrower.Status);
        builder.HasIndex(borrower => borrower.Name);
        builder.HasIndex(borrower => borrower.Phone);
        builder.HasIndex(borrower => borrower.AadharNumber);
    }
}
