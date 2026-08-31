using DhirDhar.Application.Abstractions.Services;
using DhirDhar.Infrastructure.Services;

namespace DhirDhar.Infrastructure.Tests;

public class DateTimeServiceTests
{
    [Fact]
    public void UtcNow_ReturnsValueCloseToSystemUtc()
    {
        var service = new DateTimeService();
        var difference = DateTime.UtcNow - service.UtcNow;

        Assert.InRange(Math.Abs(difference.TotalSeconds), 0, 5);
    }

    [Fact]
    public void Now_ReturnsLocalOffsetTime()
    {
        var service = new DateTimeService();

        Assert.NotEqual(DateTimeOffset.MinValue, service.Now);
    }
}
