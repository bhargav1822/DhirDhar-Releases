using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Licensing.Models;

namespace DhirDhar.Application.Licensing;

public interface ILicenseManager
{
    event EventHandler<LicenseStatus>? LicenseStatusChanged;

    LicenseStatus Status { get; }

    bool IsLicensed { get; }

    bool IsReadOnly { get; }

    bool RequiresActivation { get; }

    LicenseInfo? CurrentLicense { get; }

    string? DeviceId { get; }

    Task<LicenseValidationResult> InitializeAsync(CancellationToken cancellationToken = default);

    Task<LicenseActivationResult> ActivateAsync(string serialKey, CancellationToken cancellationToken = default);

    Task<LicenseActivationResult> RenewAsync(string newSerialKey, CancellationToken cancellationToken = default);

    Task<LicenseValidationResult> ValidateCurrentLicenseAsync(CancellationToken cancellationToken = default);
}
