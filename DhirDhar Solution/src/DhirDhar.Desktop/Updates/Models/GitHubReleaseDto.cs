using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DhirDhar.Desktop.Updates.Models;

/// <summary>
/// Represents a GitHub Release object from GitHub REST API v3.
/// </summary>
public sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; init; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; init; }

    [JsonPropertyName("assets")]
    public List<GitHubAssetDto> Assets { get; init; } = new();
}

/// <summary>
/// Represents a asset attached to a GitHub Release.
/// </summary>
public sealed class GitHubAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;
}
