using DhirDhar.Application.Common.Results;

namespace DhirDhar.Application.Tests;

public class ResultTests
{
    [Fact]
    public void Success_Result_IsSuccess_WithoutError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_Result_IsFailure_WithError()
    {
        var result = Result.Failure("something went wrong");

        Assert.True(result.IsFailure);
        Assert.Equal("something went wrong", result.Error);
    }

    [Fact]
    public void Success_TypedResult_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_TypedResult_ThrowsWhenAccessingValue()
    {
        var result = Result<int>.Failure("nope");

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }
}
