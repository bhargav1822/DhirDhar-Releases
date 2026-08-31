using DhirDhar.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DhirDhar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures persistence mapping for <see cref="Transaction"/>. The <c>Type</c>
/// enum is stored as an integer; the amount is stored via owned type mapping.
/// </summary>
public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");

        builder.HasKey(transaction => transaction.Id);

        builder.HasOne<FinancialPeriod>()
            .WithMany()
            .HasForeignKey(transaction => transaction.FinancialPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(transaction => transaction.Borrower)
            .WithMany(borrower => borrower.Transactions)
            .HasForeignKey(transaction => transaction.BorrowerId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.OwnsOne(transaction => transaction.Amount, money =>
        {
            money.Property(amount => amount.Amount)
                .HasPrecision(18, 2)
                .IsRequired();
        });

        builder.Property(transaction => transaction.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(transaction => transaction.OccurredOn)
            .HasConversion<DateTime?>();

        builder.Ignore(transaction => transaction.TransactionDate);

        builder.Property(transaction => transaction.Description)
            .HasMaxLength(500);

        builder.Property(transaction => transaction.CreatedAt)
            .HasConversion<DateTime?>();

        builder.Property(transaction => transaction.UpdatedAt)
            .HasConversion<DateTime?>();

        builder.HasIndex(transaction => transaction.BorrowerId);
        builder.HasIndex(transaction => transaction.OccurredOn);
        builder.HasIndex(transaction => transaction.Type);
        builder.HasIndex(transaction => transaction.FinancialPeriodId);
        builder.HasIndex(transaction => new { transaction.BorrowerId, transaction.OccurredOn });
    }
}
