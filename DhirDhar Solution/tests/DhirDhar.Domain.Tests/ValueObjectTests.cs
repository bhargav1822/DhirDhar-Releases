using DhirDhar.Domain.Common;

namespace DhirDhar.Domain.Tests;

public class ValueObjectTests
{
    [Fact]
    public void ValueObjects_WithSameValues_AreEqual()
    {
        var first = new TestValueObject("alpha", 42);
        var second = new TestValueObject("alpha", 42);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ValueObjects_WithDifferentValues_AreNotEqual()
    {
        var first = new TestValueObject("alpha", 42);
        var second = new TestValueObject("beta", 42);

        Assert.False(first.Equals(second));
        Assert.True(first != second);
    }

    private sealed class TestValueObject : ValueObject
    {
        public TestValueObject(string name, int number)
        {
            Name = name;
            Number = number;
        }

        public string Name { get; }

        public int Number { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Name;
            yield return Number;
        }
    }
}
