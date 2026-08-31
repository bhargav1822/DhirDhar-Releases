using System;
using DhirDhar.Application.Updates.Helpers;
using Xunit;

namespace DhirDhar.Application.Tests;

public sealed class SemanticVersionTests
{
    [Fact]
    public void Parse_ValidVersions_ReturnsCorrectValues()
    {
        Assert.True(SemanticVersion.TryParse("1.0.0", out var v1));
        Assert.Equal(1, v1.Major);
        Assert.Equal(0, v1.Minor);
        Assert.Equal(0, v1.Patch);
        Assert.False(v1.IsPreRelease);

        Assert.True(SemanticVersion.TryParse("v1.1.0", out var v2));
        Assert.Equal(1, v2.Major);
        Assert.Equal(1, v2.Minor);
        Assert.Equal(0, v2.Patch);
        Assert.False(v2.IsPreRelease);

        Assert.True(SemanticVersion.TryParse("2.0.0-beta.1", out var v3));
        Assert.Equal(2, v3.Major);
        Assert.True(v3.IsPreRelease);
    }

    [Fact]
    public void Comparison_EvaluatesOlderAndNewerVersionsCorrectly()
    {
        Assert.True(SemanticVersion.TryParse("1.0.0", out var v100));
        Assert.True(SemanticVersion.TryParse("1.0.1", out var v101));
        Assert.True(SemanticVersion.TryParse("v1.1.0", out var update));
        Assert.True(SemanticVersion.TryParse("0.9.0", out var older));

        Assert.True(SemanticVersion.TryParse("1.1.1", out var v111));
        Assert.True(SemanticVersion.TryParse("1.1", out var v11));
        Assert.True(v111 > v11);
        Assert.True(v111 > update);
        Assert.True(v101 > v100);
        Assert.True(update > v101);
        Assert.True(v100 > older);
        Assert.True(v101 >= v100);
        Assert.True(v100 <= v101);
    }
}
