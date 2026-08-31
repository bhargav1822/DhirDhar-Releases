using System;

namespace DhirDhar.Application.Licensing.Models;

public enum LicenseStatus
{
    NotActivated,
    Active,
    ExpiringSoon,
    Expired,
    Invalid
}

public sealed record LicenseInfo(
    string Product,
    string LicenseId,
    string CustomerName,
    string CustomerEmail,
    string Edition,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    int DaysRemaining,
    LicenseStatus Status,
    string? BoundDeviceId,
    DateTime? ActivatedAt,
    string? IssuanceId = null,
    string? PreviousLicenseId = null,
    bool IsRenewal = false)
{
    public string FormattedIssuedAt => IssuedAt.ToString("dd-MMM-yyyy");
    public string FormattedExpiresAt => ExpiresAt.ToString("dd-MMM-yyyy");
    public bool IsActive => Status == LicenseStatus.Active || Status == LicenseStatus.ExpiringSoon;
    public bool IsExpiringSoon => Status == LicenseStatus.ExpiringSoon;
    public bool IsExpired => Status == LicenseStatus.Expired;
}

public sealed record LicenseValidationResult(
    bool IsValid,
    LicenseStatus Status,
    string Message,
    LicenseInfo? LicenseInfo);

public sealed record LicenseActivationResult(
    bool Success,
    LicenseStatus Status,
    string Message,
    LicenseInfo? LicenseInfo);
