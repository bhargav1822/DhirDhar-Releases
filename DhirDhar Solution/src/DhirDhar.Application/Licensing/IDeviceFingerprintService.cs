namespace DhirDhar.Application.Licensing;

public interface IDeviceFingerprintService
{
    /// <summary>
    /// Gets the stable, deterministic hardware device fingerprint for this Windows PC.
    /// </summary>
    string GetDeviceFingerprint();

    /// <summary>
    /// Validates if the provided device fingerprint matches the current machine.
    /// </summary>
    bool ValidateDeviceFingerprint(string expectedFingerprint);
}
