using DhirDhar.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DhirDhar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures persistence mapping for <see cref="Loan"/>. The <see cref="DhirDhar.Domain.ValueObjects.Money"/>
/// principal is stored as a plain decimal column via owned type mapping.
/// </summary>
public sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("Loans");

        builder.HasKey(loan => loan.Id);

        builder.HasOne(loan => loan.Borrower)
            .WithMany(borrower => borrower.Loans)
            .HasForeignKey(loan => loan.BorrowerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsOne(loan => loan.Principal, money =>
        {
            money.Property(principal => principal.Amount)
                .HasPrecision(18, 2)
                .IsRequired();
        });

        builder.Property(loan => loan.InterestRatePercent)
            .HasPrecision(9, 4)
            .IsRequired();

        builder.Property(loan => loan.InterestFrequency)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(loan => loan.IssueDate)
            .HasConversion<DateTime?>();

        builder.Property(loan => loan.IsRepaid)
            .IsRequired();

        builder.Property(loan => loan.CreatedAt)
            .HasConversion<DateTime?>();

        builder.Property(loan => loan.UpdatedAt)
            .HasConversion<DateTime?>();
    }
}
