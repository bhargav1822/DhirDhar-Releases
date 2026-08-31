using System;
using DhirDhar.Application.Transactions;
using Xunit;

namespace DhirDhar.Application.Tests;

public class MonetaryAmountParserTests
{
    [Theory]
    [InlineData("25000", 25000.00)]
    [InlineData("1", 1.00)]
    [InlineData("100", 100.00)]
    [InlineData("25000.50", 25000.50)]
    [InlineData("100000.00", 100000.00)]
    [InlineData("25,000", 25000.00)]
    [InlineData("1,00,000.00", 100000.00)]
    [InlineData("₹25000", 25000.00)]
    [InlineData("₹ 25,000.50", 25000.50)]
    [InlineData("Rs. 25000", 25000.00)]
    [InlineData("Rs 100000", 100000.00)]
    [InlineData("INR 25000", 25000.00)]
    [InlineData("૨૫૦૦૦", 25000.00)]       // Gujarati digits 25000
    [InlineData("૨૫૦૦૦.૫૦", 25000.50)]   // Gujarati digits 25000.50
    [InlineData("२५०००", 25000.00)]       // Devanagari/Hindi digits 25000
    [InlineData("२५०००.५०", 25000.50)]   // Devanagari/Hindi digits 25000.50
    [InlineData(" 25000 ", 25000.00)]
    public void TryParse_ValidMonetaryInputs_ReturnsExpectedDecimal(string input, decimal expected)
    {
        var result = MonetaryAmountParser.TryParse(input, out var amount);
        Assert.True(result, $"Expected TryParse to succeed for '{input}'");
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-25000")]
    [InlineData("-1")]
    [InlineData("-0.50")]
    [InlineData("abc")]
    [InlineData("25a00")]
    [InlineData("25.00.00")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("₹")]
    [InlineData(".")]
    [InlineData(",")]
    public void TryParse_InvalidOrNonPositiveInputs_ReturnsFalse(string? input)
    {
        var result = MonetaryAmountParser.TryParse(input, out var amount);
        Assert.False(result, $"Expected TryParse to fail for '{input}'");
        Assert.Equal(0m, amount);
    }
}
