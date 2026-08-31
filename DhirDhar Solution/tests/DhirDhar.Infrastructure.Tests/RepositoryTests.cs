using DhirDhar.Infrastructure.Persistence.Repositories;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DhirDhar.Infrastructure.Tests;

public class RepositoryTests
{
    [Fact]
    public async Task AddAndGetById_Works()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        var repository = new Repository<TestPersistenceEntity>(context);
        var entity = new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "Alpha" };

        await repository.AddAsync(entity);
        await context.SaveChangesAsync();

        var loaded = await repository.GetByIdAsync(entity.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Alpha", loaded!.Name);
    }

    [Fact]
    public async Task GetAll_ReturnsAddedEntities()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        var repository = new Repository<TestPersistenceEntity>(context);

        await repository.AddAsync(new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "One" });
        await repository.AddAsync(new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "Two" });
        await context.SaveChangesAsync();

        var all = await repository.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Update_PersistsChange()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        var repository = new Repository<TestPersistenceEntity>(context);
        var entity = new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "Original" };
        await repository.AddAsync(entity);
        await context.SaveChangesAsync();

        entity.Name = "Updated";
        await repository.UpdateAsync(entity);
        await context.SaveChangesAsync();

        var loaded = await repository.GetByIdAsync(entity.Id);
        Assert.Equal("Updated", loaded!.Name);
    }

    [Fact]
    public async Task Delete_RemovesEntity()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        var repository = new Repository<TestPersistenceEntity>(context);
        var entity = new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "ToDelete" };
        await repository.AddAsync(entity);
        await context.SaveChangesAsync();

        await repository.DeleteAsync(entity);
        await context.SaveChangesAsync();

        Assert.False(await repository.ExistsAsync(entity.Id));
        Assert.Null(await repository.GetByIdAsync(entity.Id));
    }

    [Fact]
    public async Task Exists_ReflectsEntityPresence()
    {
        using var temp = new TempDatabase();
        await using var context = new TestPersistenceDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();
        var repository = new Repository<TestPersistenceEntity>(context);
        var entity = new TestPersistenceEntity { Id = Guid.NewGuid(), Name = "Present" };
        await repository.AddAsync(entity);
        await context.SaveChangesAsync();

        Assert.True(await repository.ExistsAsync(entity.Id));
        Assert.False(await repository.ExistsAsync(Guid.NewGuid()));
    }
}
