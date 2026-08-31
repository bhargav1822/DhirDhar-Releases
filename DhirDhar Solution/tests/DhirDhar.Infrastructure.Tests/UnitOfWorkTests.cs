using DhirDhar.Infrastructure.Persistence.Repositories;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DhirDhar.Infrastructure.Tests;

public class UnitOfWorkTests
{
    [Fact]
    public async Task Commit_PersistsChanges()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        await using var unitOfWork = new UnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();
        var repository = unitOfWork.GetRepository<TestPersistenceEntity>();
        await repository.AddAsync(new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "One" });
        await repository.AddAsync(new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "Two" });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitAsync();

        await AssertPersistedCountAsync(temp, 2);
    }

    [Fact]
    public async Task Rollback_LeavesNoPartialChanges()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        await using var unitOfWork = new UnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();
        var repository = unitOfWork.GetRepository<TestPersistenceEntity>();
        await repository.AddAsync(new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "One" });
        await repository.AddAsync(new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "Two" });
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.RollbackAsync();

        await AssertPersistedCountAsync(temp, 0);
    }

    [Fact]
    public async Task Rollback_WhenExceptionOccurs_LeavesNoPartialChanges()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        await using var unitOfWork = new UnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();
        var repository = unitOfWork.GetRepository<TestPersistenceEntity>();
        await repository.AddAsync(new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "WillRollBack" });

        try
        {
            await unitOfWork.SaveChangesAsync();
            throw new InvalidOperationException("Simulated operation failure.");
        }
        catch
        {
            await unitOfWork.RollbackAsync();
        }

        await AssertPersistedCountAsync(temp, 0);
    }

    [Fact]
    public async Task BeginTransaction_WhenAlreadyActive_Throws()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        await using var unitOfWork = new UnitOfWork(context);

        await unitOfWork.BeginTransactionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.BeginTransactionAsync());
    }

    [Fact]
    public async Task Commit_WithoutTransaction_Throws()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        await using var unitOfWork = new UnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.CommitAsync());
    }

    [Fact]
    public async Task Rollback_WithoutTransaction_Throws()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        await using var unitOfWork = new UnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.RollbackAsync());
    }

    [Fact]
    public async Task SaveChanges_WithoutTransaction_PersistsChanges()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        await using var unitOfWork = new UnitOfWork(context);

        var repository = unitOfWork.GetRepository<TestPersistenceEntity>();
        await repository.AddAsync(new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "Autocommitted" });
        await unitOfWork.SaveChangesAsync();

        await AssertPersistedCountAsync(temp, 1);
    }

    private static async Task AssertPersistedCountAsync(TempDatabase temp, int expected)
    {
        await using var verification = new TestPersistenceDbContext(temp.CreateOptions());
        var count = await verification.Set<TestPersistenceEntity>().CountAsync();
        Assert.Equal(expected, count);
    }
}
