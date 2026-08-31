using DhirDhar.Domain.Entities;
using DhirDhar.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;

namespace DhirDhar.Infrastructure.Persistence;

/// <summary>
/// The Entity Framework Core database context for the application. It is configured for
/// SQLite and constructed exclusively through dependency injection or the design-time factory.
/// The domain layer remains independent of Entity Framework Core.
/// </summary>
public class DhirDharDbContext : DbContext
{
    public static event Action? OnDatabaseSaved;

    public DhirDharDbContext(DbContextOptions<DhirDharDbContext> options)
        : base(options)
    {
    }

    public DbSet<Borrower> Borrowers => Set<Borrower>();

    public DbSet<Loan> Loans => Set<Loan>();

    public DbSet<FinancialPeriod> FinancialPeriods => Set<FinancialPeriod>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Domain.Entities.Report> Reports => Set<Domain.Entities.Report>();

    public DbSet<Domain.Entities.AuditEntry> AuditEntries => Set<Domain.Entities.AuditEntry>();

    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();

    public DbSet<UserTextTranslation> UserTextTranslations => Set<UserTextTranslation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DhirDharDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries().ToList();
        var result = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (result > 0)
        {
            OnDatabaseSaved?.Invoke();
            InvalidateCachesForEntries(entries);
        }
        return result;
    }

    public override int SaveChanges()
    {
        var entries = ChangeTracker.Entries().ToList();
        var result = base.SaveChanges();
        if (result > 0)
        {
            OnDatabaseSaved?.Invoke();
            InvalidateCachesForEntries(entries);
        }
        return result;
    }

    private void InvalidateCachesForEntries(List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> entries)
    {
        try
        {
            var cache = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<DhirDhar.Application.Caching.ICacheService>(this);
            if (cache != null)
            {
                cache.Remove("dashboard_summary");
                cache.RemoveByPrefix("borrowers_page_");
                cache.RemoveByPrefix("search_query_");
                foreach (var entry in entries)
                {
                    if (entry.Entity is Borrower b)
                    {
                        cache.Remove($"borrower_id_{b.Id}");
                        if (!string.IsNullOrWhiteSpace(b.BorrowerNumber))
                        {
                            var clean = b.BorrowerNumber.Trim().TrimStart('#').Trim();
                            cache.Remove($"borrower_num_{clean}");
                            cache.Remove($"borrower_num_#{clean}");
                        }
                    }
                    else if (entry.Entity is Transaction t && t.BorrowerId.HasValue)
                    {
                        cache.Remove($"borrower_id_{t.BorrowerId.Value}");
                    }
                }
            }
        }
        catch
        {
        }
    }
}
