using DhirDhar.Domain.Common;

namespace DhirDhar.Domain.Tests;

public class EntityTests
{
    [Fact]
    public void TwoEntities_WithSameId_AreEqual()
    {
        var id = Guid.NewGuid();
        var first = new TestEntity(id);
        var second = new TestEntity(id);

        Assert.Equal(first, second);
        Assert.True(first == second);
    }

    [Fact]
    public void TwoEntities_WithDifferentIds_AreNotEqual()
    {
        var first = new TestEntity(Guid.NewGuid());
        var second = new TestEntity(Guid.NewGuid());

        Assert.True(first.Id != second.Id);
        Assert.True(first != second);
    }

    [Fact]
    public void Entity_WithDefaultConstructor_GeneratesUniqueId()
    {
        var first = new TestEntity();
        var second = new TestEntity();

        Assert.True(first.Id != second.Id);
        Assert.NotEqual(Guid.Empty, first.Id);
    }

    [Fact]
    public void Entities_OfDifferentTypes_AreNotEqual_EvenWithSameId()
    {
        var id = Guid.NewGuid();

        Assert.False(new TestEntity(id).Equals(new OtherTestEntity(id)));
    }

    private sealed class TestEntity : Entity
    {
        public TestEntity()
        {
        }

        public TestEntity(Guid id)
            : base(id)
        {
        }
    }

    private sealed class OtherTestEntity : Entity
    {
        public OtherTestEntity(Guid id)
            : base(id)
        {
        }
    }
}
