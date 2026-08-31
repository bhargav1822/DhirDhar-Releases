using System;
using DhirDhar.Application.QrCode;
using DhirDhar.Infrastructure.QrCode;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class QrCodeServiceTests
{
    private readonly IQrCodeService _service = new QrCodeService();

    [Theory]
    [InlineData("DJ102", "DHIRDHAR|ACCOUNT|DJ102")]
    [InlineData("DJ135", "DHIRDHAR|ACCOUNT|DJ135")]
    [InlineData("DJ148", "DHIRDHAR|ACCOUNT|DJ148")]
    [InlineData("ACC-001", "DHIRDHAR|ACCOUNT|ACC-001")]
    public void FormatPayload_ShouldProduceStandardDhirDharFormat(string borrowerNumber, string expectedPayload)
    {
        var payload = _service.FormatPayload(borrowerNumber);
        Assert.Equal(expectedPayload, payload);
    }

    [Theory]
    [InlineData("DHIRDHAR|ACCOUNT|DJ102", "DJ102")]
    [InlineData("dhirdhar|account|dj102", "dj102")]
    [InlineData("  DHIRDHAR|ACCOUNT|DJ135  ", "DJ135")]
    [InlineData("DHIRDHAR|ACCOUNT|#DJ148", "DJ148")]
    [InlineData("DJ102", "DJ102")]
    [InlineData("#DJ102", "DJ102")]
    public void TryParsePayload_ValidInputs_ShouldReturnTrueAndExtractedBorrowerNumber(string input, string expectedNumber)
    {
        var result = _service.TryParsePayload(input, out var extracted);
        Assert.True(result);
        Assert.Equal(expectedNumber, extracted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("DHIRDHAR|")]
    [InlineData("DHIRDHAR|ACCOUNT|")]
    [InlineData("DHIRDHAR|OTHER|123")]
    [InlineData("OTHERAPP|ACCOUNT|DJ102")]
    public void TryParsePayload_InvalidInputs_ShouldReturnFalse(string? input)
    {
        var result = _service.TryParsePayload(input!, out var extracted);
        Assert.False(result);
        Assert.Equal(string.Empty, extracted);
    }

    [Fact]
    public void GeneratePngBytes_ValidBorrowerNumber_ShouldReturnValidPngHeader()
    {
        var bytes = _service.GeneratePngBytes("DJ102", 10);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length > 50);

        // Standard PNG Magic Header: 0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
        Assert.Equal(0x0D, bytes[4]);
        Assert.Equal(0x0A, bytes[5]);
        Assert.Equal(0x1A, bytes[6]);
        Assert.Equal(0x0A, bytes[7]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void GeneratePngBytes_EmptyOrNull_ShouldThrowArgumentException(string? invalidNumber)
    {
        Assert.Throws<ArgumentException>(() => _service.GeneratePngBytes(invalidNumber!));
    }
}
