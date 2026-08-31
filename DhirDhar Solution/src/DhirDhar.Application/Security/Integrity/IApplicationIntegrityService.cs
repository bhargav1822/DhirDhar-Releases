using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Security.Integrity;

public enum IntegrityScanCategory
{
    Initialization,
    FileEnumeration,
    Configurations,
    ApplicationBinaries,
    RuntimeDependencies,
    Resources,
    ManifestVerification,
    Completed
}

public enum IntegrityFailureType
{
    None,
    FileModified,
    FileMissing,
    ManifestMissing,
    ManifestCorrupted,
    SignatureInvalid,
    VersionMismatch,
    AccessDenied,
    UnexpectedLayout
}

public sealed record IntegrityScanProgress(
    IntegrityScanCategory Category,
    int TotalItems,
    int ProcessedItems,
    int OverallProgressPercentage,
    string CurrentItemName);

public sealed record IntegrityDiagnosticDetail(
    string RelativePath,
    string ExpectedHash,
    string ActualHash,
    long ExpectedSize,
    long ActualSize,
    string Status);

public sealed record BinaryIntegrityResult(
    bool IsValid,
    string StatusMessage,
    IReadOnlyList<string> TamperedFiles,
    IReadOnlyList<string> MissingFiles,
    DateTime VerifiedAtUtc,
    int TotalFilesScanned = 0,
    IntegrityFailureType FailureType = IntegrityFailureType.None,
    IReadOnlyList<IntegrityDiagnosticDetail>? DiagnosticDetails = null,
    string ApplicationVersion = "2.0.0",
    string ManifestVersion = "2.0.0");

public sealed class IntegrityFileDto
{
    [JsonPropertyName("RelativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("Sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("SizeBytes")]
    public long SizeBytes { get; set; }
}

public sealed class IntegrityManifestDto
{
    [JsonPropertyName("ApplicationVersion")]
    public string ApplicationVersion { get; set; } = "2.0.0";

    [JsonPropertyName("GeneratedAtUtc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("Files")]
    public List<IntegrityFileDto> Files { get; set; } = new();

    [JsonPropertyName("Signature")]
    public string Signature { get; set; } = string.Empty;
}

public interface IApplicationIntegrityService
{
    Task<BinaryIntegrityResult> VerifyApplicationIntegrityAsync(
        IProgress<IntegrityScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    string GenerateIntegrityManifest(string baseDirectory);
}
