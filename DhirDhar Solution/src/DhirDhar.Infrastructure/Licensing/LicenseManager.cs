using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Licensing.Models;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Licensing;

public sealed class LicenseManager : ILicenseManager
{
    private readonly ILicenseStorageService _storageService;
    private readonly IDeviceFingerprintService _fingerprintService;
    private readonly ILogger<LicenseManager>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private LicenseStatus _status = LicenseStatus.NotActivated;
    private LicenseInfo? _currentLicense;
    private bool _isInitialized;

    public event EventHandler<LicenseStatus>? LicenseStatusChanged;

    public LicenseManager(
        ILicenseStorageService storageService,
        IDeviceFingerprintService fingerprintService,
        ILogger<LicenseManager>? logger = null)
    {
        _storageService = storageService;
        _fingerprintService = fingerprintService;
        _logger = logger;
    }

    public LicenseStatus Status => _status;

    public bool IsLicensed => _status == LicenseStatus.Active || _status == LicenseStatus.ExpiringSoon;

    public bool IsReadOnly => _status == LicenseStatus.Expired;

    public bool RequiresActivation => _status == LicenseStatus.NotActivated || _status == LicenseStatus.Invalid;

    public LicenseInfo? CurrentLicense => _currentLicense;

    public bool IsInitialized => _isInitialized;

    public string? DeviceId => _fingerprintService.GetDeviceFingerprint();

    public async Task<LicenseValidationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await ValidateStoredLicenseInternalAsync(cancellationToken).ConfigureAwait(false);
            _isInitialized = true;
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LicenseValidationResult> ValidateCurrentLicenseAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ValidateStoredLicenseInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LicenseActivationResult> ActivateAsync(string serialKey, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(serialKey))
            {
                return new LicenseActivationResult(false, LicenseStatus.NotActivated, "Please enter a valid serial key.", null);
            }

            var (isValid, payload, errorMessage) = LicenseDecoder.VerifySerialKey(serialKey);
            if (!isValid || payload is null)
            {
                _logger?.LogWarning("License activation failed: {Error}", errorMessage);
                return new LicenseActivationResult(false, LicenseStatus.Invalid, errorMessage, null);
            }

            var nowUtc = DateTime.UtcNow;
            if (nowUtc >= payload.ExpiresAt)
            {
                var expiredMsg = $"This serial key expired on {payload.ExpiresAt:dd-MMM-yyyy}.";
                _logger?.LogWarning("License activation rejected: Key is already expired ({ExpiresAt}).", payload.ExpiresAt);
                return new LicenseActivationResult(false, LicenseStatus.Expired, expiredMsg, null);
            }

            var currentDeviceId = _fingerprintService.GetDeviceFingerprint();

            if (!string.IsNullOrWhiteSpace(payload.DeviceBinding))
            {
                bool isDeviceMatch = false;
                if (payload.DeviceBinding.StartsWith("HW-", StringComparison.OrdinalIgnoreCase))
                {
                    var currentHash = LicenseDecoder.ComputeHardwareIdHash(currentDeviceId);
                    var expectedHashStr = payload.DeviceBinding.Substring(3);
                    if (uint.TryParse(expectedHashStr, System.Globalization.NumberStyles.HexNumber, null, out var expectedHash))
                    {
                        isDeviceMatch = currentHash == expectedHash;
                    }
                }
                else
                {
                    isDeviceMatch = _fingerprintService.ValidateDeviceFingerprint(payload.DeviceBinding);
                }

                if (!isDeviceMatch)
                {
                    var devMismatchError = "This license is bound to a specific PC and cannot be activated on this machine.";
                    _logger?.LogWarning(devMismatchError);
                    return new LicenseActivationResult(false, LicenseStatus.Invalid, devMismatchError, null);
                }
            }

            var activationRecord = new StoredActivation(
                SerialKey: serialKey.Trim(),
                BoundDeviceId: currentDeviceId,
                ActivatedAt: nowUtc,
                LastVerifiedAt: nowUtc,
                LastKnownSystemDate: nowUtc,
                Checksum: string.Empty,
                CustomerName: payload.CustomerName,
                CustomerEmail: payload.CustomerEmail);

            await _storageService.SaveActivationAsync(activationRecord, cancellationToken).ConfigureAwait(false);

            var daysRemaining = Math.Max(0, (int)(payload.ExpiresAt.Date - nowUtc.Date).TotalDays);
            var newStatus = daysRemaining <= 30 ? LicenseStatus.ExpiringSoon : LicenseStatus.Active;

            var prevLicenseId = payload.PreviousLicenseId;
            if (string.IsNullOrWhiteSpace(prevLicenseId) && (payload.IsRenewal || string.Equals(payload.Edition, "Renewal", StringComparison.OrdinalIgnoreCase)))
            {
                prevLicenseId = _currentLicense?.LicenseId;
            }

            _currentLicense = new LicenseInfo(
                Product: payload.Product,
                LicenseId: payload.LicenseId,
                CustomerName: payload.CustomerName,
                CustomerEmail: payload.CustomerEmail,
                Edition: payload.Edition,
                IssuedAt: payload.IssuedAt,
                ExpiresAt: payload.ExpiresAt,
                DaysRemaining: daysRemaining,
                Status: newStatus,
                BoundDeviceId: currentDeviceId,
                ActivatedAt: nowUtc,
                IssuanceId: payload.IssuanceId,
                PreviousLicenseId: prevLicenseId,
                IsRenewal: payload.IsRenewal || string.Equals(payload.Edition, "Renewal", StringComparison.OrdinalIgnoreCase));

            SetStatus(newStatus);

            _logger?.LogInformation("DhirDhar license activated successfully for '{CustomerName}', LicenseId='{LicenseId}', Expires='{ExpiresAt:dd-MMM-yyyy}'.",
                payload.CustomerName, payload.LicenseId, payload.ExpiresAt);

            return new LicenseActivationResult(true, newStatus, "License activated successfully.", _currentLicense);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during license activation.");
            return new LicenseActivationResult(false, LicenseStatus.Invalid, $"Activation error: {ex.Message}", null);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LicenseActivationResult> RenewAsync(string newSerialKey, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Attempting offline license renewal...");
        return await ActivateAsync(newSerialKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LicenseValidationResult> ValidateStoredLicenseInternalAsync(CancellationToken cancellationToken)
    {
        var stored = await _storageService.LoadActivationAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null || string.IsNullOrWhiteSpace(stored.SerialKey))
        {
            SetStatus(LicenseStatus.NotActivated);
            _currentLicense = null;
            return new LicenseValidationResult(false, LicenseStatus.NotActivated, "No license found on this device.", null);
        }

        var (isValid, payload, errorMessage) = LicenseDecoder.VerifySerialKey(
            stored.SerialKey, 
            candidateCustomerName: stored.CustomerName, 
            candidateCustomerEmail: stored.CustomerEmail);
        if (!isValid || payload is null)
        {
            SetStatus(LicenseStatus.Invalid);
            _currentLicense = null;
            return new LicenseValidationResult(false, LicenseStatus.Invalid, errorMessage, null);
        }

        // Verify Device Binding (1 License = 1 PC)
        var isDeviceValid = _fingerprintService.ValidateDeviceFingerprint(stored.BoundDeviceId);
        if (!isDeviceValid)
        {
            SetStatus(LicenseStatus.Invalid);
            _currentLicense = null;
            var devError = "This license is registered to a different Windows PC and cannot be used on this machine.";
            _logger?.LogWarning(devError);
            return new LicenseValidationResult(false, LicenseStatus.Invalid, devError, null);
        }

        var nowUtc = DateTime.UtcNow;

        // Anti-tamper: Detect significant clock rollback (> 2 days in past compared to last known run)
        if (nowUtc < stored.LastKnownSystemDate.AddDays(-2))
        {
            SetStatus(LicenseStatus.Invalid);
            var clockError = "System clock tampering detected. Please correct your system date and time.";
            _logger?.LogWarning(clockError);
            return new LicenseValidationResult(false, LicenseStatus.Invalid, clockError, null);
        }

        var daysRemaining = (int)(payload.ExpiresAt.Date - nowUtc.Date).TotalDays;

        var effectiveCustomerName = !string.IsNullOrWhiteSpace(payload.CustomerName) && payload.CustomerName != "DhirDhar Customer"
            ? payload.CustomerName
            : (!string.IsNullOrWhiteSpace(stored.CustomerName) ? stored.CustomerName : payload.CustomerName);
        var effectiveCustomerEmail = !string.IsNullOrWhiteSpace(payload.CustomerEmail) && payload.CustomerEmail != "customer@dhirdhar.com"
            ? payload.CustomerEmail
            : (!string.IsNullOrWhiteSpace(stored.CustomerEmail) ? stored.CustomerEmail : payload.CustomerEmail);

        if (nowUtc >= payload.ExpiresAt || daysRemaining < 0)
        {
            daysRemaining = 0;
            SetStatus(LicenseStatus.Expired);
            _currentLicense = new LicenseInfo(
                Product: payload.Product,
                LicenseId: payload.LicenseId,
                CustomerName: effectiveCustomerName,
                CustomerEmail: effectiveCustomerEmail,
                Edition: payload.Edition,
                IssuedAt: payload.IssuedAt,
                ExpiresAt: payload.ExpiresAt,
                DaysRemaining: 0,
                Status: LicenseStatus.Expired,
                BoundDeviceId: stored.BoundDeviceId,
                ActivatedAt: stored.ActivatedAt,
                IssuanceId: payload.IssuanceId,
                PreviousLicenseId: payload.PreviousLicenseId,
                IsRenewal: payload.IsRenewal);

            return new LicenseValidationResult(false, LicenseStatus.Expired, $"Your license expired on {payload.ExpiresAt:dd-MMM-yyyy}.", _currentLicense);
        }

        var status = daysRemaining <= 30 ? LicenseStatus.ExpiringSoon : LicenseStatus.Active;
        SetStatus(status);

        _currentLicense = new LicenseInfo(
            Product: payload.Product,
            LicenseId: payload.LicenseId,
            CustomerName: effectiveCustomerName,
            CustomerEmail: effectiveCustomerEmail,
            Edition: payload.Edition,
            IssuedAt: payload.IssuedAt,
            ExpiresAt: payload.ExpiresAt,
            DaysRemaining: daysRemaining,
            Status: status,
            BoundDeviceId: stored.BoundDeviceId,
            ActivatedAt: stored.ActivatedAt,
            IssuanceId: payload.IssuanceId,
            PreviousLicenseId: payload.PreviousLicenseId,
            IsRenewal: payload.IsRenewal);

        // Update last verified timestamp and system date
        try
        {
            var updated = stored with
            {
                LastVerifiedAt = nowUtc,
                LastKnownSystemDate = nowUtc > stored.LastKnownSystemDate ? nowUtc : stored.LastKnownSystemDate
            };
            await _storageService.SaveActivationAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update last verified date in stored activation.");
        }

        return new LicenseValidationResult(true, status, "License is valid and active.", _currentLicense);
    }

    private void SetStatus(LicenseStatus newStatus)
    {
        var oldStatus = _status;
        _status = newStatus;
        if (oldStatus != newStatus)
        {
            LicenseStatusChanged?.Invoke(this, newStatus);
        }
    }
}
