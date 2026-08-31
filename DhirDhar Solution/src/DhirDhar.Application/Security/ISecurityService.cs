namespace DhirDhar.Application.Security;

public interface ISecurityService
{
    bool IsLockEnabled { get; }
    bool IsLocked { get; }
    string AutoLockSetting { get; }
    int FailedAttempts { get; }

    Task<bool> EnableLockAsync(string pin, CancellationToken cancellationToken = default);
    Task<bool> DisableLockAsync(string pin, CancellationToken cancellationToken = default);
    Task<bool> ChangePinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default);
    Task<bool> UnlockAsync(string pin, CancellationToken cancellationToken = default);
    Task LockAsync(CancellationToken cancellationToken = default);
    Task SetAutoLockAsync(string setting, CancellationToken cancellationToken = default);
    void RecordActivity();
    event EventHandler? LockStateChanged;
}
