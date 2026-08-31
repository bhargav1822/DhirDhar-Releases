namespace DhirDhar.Infrastructure.Licensing;

/// <summary>
/// Contains the embedded cryptographic PUBLIC verification key for validating DhirDhar licenses.
/// Note: The private signing key is strictly kept within the offline License Generator and NEVER embedded here.
/// </summary>
public static class LicenseVerificationKey
{
    public const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEx5L8QLG6AScIeADmTZbxUZhmVn5t
gsS6ALUdFVjrC3KnQMU70oaAIpEEa90Pt0F1apDusYVwT6TI9Hh4DTVMxg==
-----END PUBLIC KEY-----";
}
