using System;

namespace DhirDhar.Domain.Entities;

/// <summary>
/// A single application-level setting stored as a key/value pair. The key is the
/// primary key and the value holds the setting's payload.
/// </summary>
public sealed class ApplicationSetting
{
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 500;

    private ApplicationSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public ApplicationSetting(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key.Trim();
        Value = value ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public void UpdateValue(string? value)
    {
        Value = value ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }
}
