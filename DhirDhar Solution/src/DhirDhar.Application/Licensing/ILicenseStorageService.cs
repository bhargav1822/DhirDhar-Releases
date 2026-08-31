using System;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Licensing;

public sealed record StoredActivation(
    string SerialKey,
    string BoundDeviceId,
    DateTime ActivatedAt,
    DateTime LastVerifiedAt,
    DateTime LastKnownSystemDate,
    string Checksum,
    string? CustomerName = null,
    string? CustomerEmail = null);

public interface ILicenseStorageService
{
    Task<StoredActivation?> LoadActivationAsync(CancellationToken cancellationToken = default);

    Task SaveActivationAsync(StoredActivation activation, CancellationToken cancellationToken = default);

    Task ClearActivationAsync(CancellationToken cancellationToken = default);
}
