using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DhirDhar.Infrastructure.Tests.Persistence;

/// <summary>
/// Derives from <see cref="DhirDharDbContext"/> and adds a test-only entity so the generic
/// repository and unit of work can be exercised without adding speculative business entities
/// to the production data model.
/// </summary>
public sealed class TestPersistenceDbContext : DhirDharDbContext
{
    public TestPersistenceDbContext(DbContextOptions<DhirDharDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TestPersistenceEntity>(entity =>
        {
            entity.ToTable("TestEntities");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });
    }
}
