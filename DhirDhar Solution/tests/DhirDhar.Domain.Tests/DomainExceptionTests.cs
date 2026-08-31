using DhirDhar.Domain.Common;

namespace DhirDhar.Domain.Tests;

public class DomainExceptionTests
{
    [Fact]
    public void DomainException_PreservesMessage()
    {
        var exception = new DomainException("Domain invariant violated.");

        Assert.Equal("Domain invariant violated.", exception.Message);
    }

    [Fact]
    public void DomainException_PreservesInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var exception = new DomainException("outer", inner);

        Assert.Same(inner, exception.InnerException);
    }
}
