using System;
using System.Security.Cryptography;
using System.Text;
using DhirDhar.Application.Security;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Security;

public sealed class SecurityService : ISecurityService
{
    private const int SaltSize = 32;
    private const int HashSize = 32;
    private const int Pbkdf2Iterations = 100_000;
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 5;

    private readonly ILogger<SecurityService> _logger;
    private byte[]? _storedHash;
    private byte[]? _storedSalt;
    private DateTime _lastActivity = DateTime.UtcNow;
    private DateTime? _lockoutEndTime;

    public bool IsLockEnabled { get; private set; }
    public bool IsLocked { get; private set; }
    public string AutoLockSetting { get; private set; } = "Never";
    public int FailedAttempts { get; private set; }

    public event EventHandler? LockStateChanged;

    public SecurityService(ILogger<SecurityService> logger)
    {
        _logger = logger;
    }

    public Task<bool> EnableLockAsync(string pin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
        {
            throw new ArgumentException("PIN must be at least 4 characters.", nameof(pin));
        }

        _storedSalt = RandomNumberGenerator.GetBytes(SaltSize);
        _storedHash = HashPin(pin, _storedSalt);
        IsLockEnabled = true;
        IsLocked = true;
        FailedAttempts = 0;

        _logger.LogInformation("ApplicationLockEnabled");
        LockStateChanged?.Invoke(this, EventArgs.Empty);

        return Task.FromResult(true);
    }

    public Task<bool> DisableLockAsync(string pin, CancellationToken cancellationToken = default)
    {
        if (!VerifyPin(pin))
        {
            RecordFailedAttempt();
            return Task.FromResult(false);
        }

        _storedHash = null;
        _storedSalt = null;
        IsLockEnabled = false;
        IsLocked = false;
        FailedAttempts = 0;

        _logger.LogInformation("ApplicationLockDisabled");
        LockStateChanged?.Invoke(this, EventArgs.Empty);

        return Task.FromResult(true);
    }

    public Task<bool> ChangePinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default)
    {
        if (!VerifyPin(currentPin))
        {
            RecordFailedAttempt();
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(newPin) || newPin.Length < 4)
        {
            throw new ArgumentException("New PIN must be at least 4 characters.", nameof(newPin));
        }

        _storedSalt = RandomNumberGenerator.GetBytes(SaltSize);
        _storedHash = HashPin(newPin, _storedSalt);
        FailedAttempts = 0;

        _logger.LogInformation("SecuritySettingChanged: PIN changed");

        return Task.FromResult(true);
    }

    public Task<bool> UnlockAsync(string pin, CancellationToken cancellationToken = default)
    {
        if (_lockoutEndTime.HasValue && DateTime.UtcNow < _lockoutEndTime.Value)
        {
            _logger.LogWarning("UnlockAttemptDuringLockout");
            return Task.FromResult(false);
        }

        if (!VerifyPin(pin))
        {
            RecordFailedAttempt();
            return Task.FromResult(false);
        }

        IsLocked = false;
        FailedAttempts = 0;
        _lockoutEndTime = null;
        RecordActivity();

        _logger.LogInformation("ApplicationUnlocked");
        LockStateChanged?.Invoke(this, EventArgs.Empty);

        return Task.FromResult(true);
    }

    public Task LockAsync(CancellationToken cancellationToken = default)
    {
        if (IsLockEnabled)
        {
            IsLocked = true;
            _logger.LogInformation("ApplicationLocked");
            LockStateChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public Task SetAutoLockAsync(string setting, CancellationToken cancellationToken = default)
    {
        AutoLockSetting = setting;
        _logger.LogInformation("SecuritySettingChanged: AutoLock={Setting}", setting);
        return Task.CompletedTask;
    }

    public void RecordActivity()
    {
        _lastActivity = DateTime.UtcNow;
    }

    private bool VerifyPin(string pin)
    {
        if (_storedHash is null || _storedSalt is null)
        {
            return false;
        }

        var hash = HashPin(pin, _storedSalt);
        return CryptographicOperations.FixedTimeEquals(hash, _storedHash);
    }

    private void RecordFailedAttempt()
    {
        FailedAttempts++;

        if (FailedAttempts >= MaxFailedAttempts)
        {
            _lockoutEndTime = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            _logger.LogWarning("AccountLockedOut: {Attempts} failed attempts", FailedAttempts);
        }
    }

    private static byte[] HashPin(string pin, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            HashSize);
    }
}
