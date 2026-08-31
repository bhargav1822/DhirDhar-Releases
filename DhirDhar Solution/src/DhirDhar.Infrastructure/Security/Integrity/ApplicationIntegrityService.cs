using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Security.Integrity;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Security.Integrity;

public sealed class ApplicationIntegrityService : IApplicationIntegrityService
{
    public const string ManifestFileName = "app_integrity.sig";
    public const string CurrentApplicationVersion = "2.1.3";
    private static readonly byte[] IntegrityHmacKey = "DhirDhar.Enterprise.ApplicationIntegrity.v1.2026"u8.ToArray();

    private readonly ILogger<ApplicationIntegrityService> _logger;
    private readonly string _baseDirectory;

    public ApplicationIntegrityService(ILogger<ApplicationIntegrityService> logger, string? baseDirectory = null)
    {
        _logger = logger;
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
    }

    public async Task<BinaryIntegrityResult> VerifyApplicationIntegrityAsync(
        IProgress<IntegrityScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(_baseDirectory, ManifestFileName);
        var diagnostics = new List<IntegrityDiagnosticDetail>();

        // 1. Initialization
        progress?.Report(new IntegrityScanProgress(
            IntegrityScanCategory.Initialization,
            TotalItems: 0,
            ProcessedItems: 0,
            OverallProgressPercentage: 5,
            CurrentItemName: "Initializing security scanner..."));

        _logger.LogInformation("[INTEGRITY SCAN] Base directory: '{BaseDir}', Manifest path: '{ManifestPath}', AppVersion: '{AppVersion}'.",
            _baseDirectory, manifestPath, CurrentApplicationVersion);

        if (!File.Exists(manifestPath))
        {
#if DEBUG
            _logger.LogInformation("[INTEGRITY] Integrity manifest not found in development/debug build. Scanning local assemblies in dev mode.");
            return await ScanDevelopmentModeAsync(progress, cancellationToken).ConfigureAwait(false);
#else
            _logger.LogWarning("[INTEGRITY WARNING] Integrity manifest '{ManifestFile}' is missing from application directory: '{ManifestPath}'.", ManifestFileName, manifestPath);
            return new BinaryIntegrityResult(
                false,
                $"Application integrity manifest is missing from '{_baseDirectory}'.",
                Array.Empty<string>(),
                new[] { ManifestFileName },
                DateTime.UtcNow,
                0,
                IntegrityFailureType.ManifestMissing,
                diagnostics,
                CurrentApplicationVersion,
                "Missing");
#endif
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var (parseSuccess, manifest, parseError) = ParseManifestJson(manifestJson);

            if (!parseSuccess || manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                _logger.LogError("[INTEGRITY ERROR] Integrity manifest is corrupted or empty: {Error}", parseError);
                return new BinaryIntegrityResult(
                    false,
                    $"Integrity manifest is corrupted: {parseError}",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    DateTime.UtcNow,
                    0,
                    IntegrityFailureType.ManifestCorrupted,
                    diagnostics,
                    CurrentApplicationVersion,
                    manifest?.ApplicationVersion ?? "Corrupted");
            }

            _logger.LogInformation("[INTEGRITY SCAN] Loaded manifest version '{ManifestVersion}' with {Count} protected file entries.",
                manifest.ApplicationVersion, manifest.Files.Count);

            // 2. File Enumeration & Classification
            progress?.Report(new IntegrityScanProgress(
                IntegrityScanCategory.FileEnumeration,
                TotalItems: manifest.Files.Count,
                ProcessedItems: 0,
                OverallProgressPercentage: 10,
                CurrentItemName: "Enumerating installed application files..."));

            // Group files into categories
            var configFiles = manifest.Files.Where(f => f.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith(".config", StringComparison.OrdinalIgnoreCase)).ToList();
            var appBinaryFiles = manifest.Files.Where(f => (f.RelativePath.StartsWith("DhirDhar", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) && (f.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))).ToList();
            var resourceFiles = manifest.Files.Where(f => f.RelativePath.EndsWith(".pri", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)).ToList();
            var runtimeFiles = manifest.Files.Except(configFiles).Except(appBinaryFiles).Except(resourceFiles).ToList();

            var tamperedFiles = new List<string>();
            var missingFiles = new List<string>();
            var accessDeniedFiles = new List<string>();
            int totalScanned = 0;

            // 3. Scan Configurations (10% -> 20%)
            for (int i = 0; i < configFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = configFiles[i];
                VerifySingleEntry(entry, tamperedFiles, missingFiles, accessDeniedFiles, diagnostics);
                totalScanned++;

                int pct = 10 + (int)Math.Round((i + 1.0) / Math.Max(1, configFiles.Count) * 10);
                progress?.Report(new IntegrityScanProgress(
                    IntegrityScanCategory.Configurations,
                    TotalItems: manifest.Files.Count,
                    ProcessedItems: totalScanned,
                    OverallProgressPercentage: Math.Min(20, pct),
                    CurrentItemName: entry.RelativePath));
            }

            // 4. Scan Application Binaries (20% -> 40%)
            for (int i = 0; i < appBinaryFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = appBinaryFiles[i];
                VerifySingleEntry(entry, tamperedFiles, missingFiles, accessDeniedFiles, diagnostics);
                totalScanned++;

                int pct = 20 + (int)Math.Round((i + 1.0) / Math.Max(1, appBinaryFiles.Count) * 20);
                progress?.Report(new IntegrityScanProgress(
                    IntegrityScanCategory.ApplicationBinaries,
                    TotalItems: manifest.Files.Count,
                    ProcessedItems: totalScanned,
                    OverallProgressPercentage: Math.Min(40, pct),
                    CurrentItemName: entry.RelativePath));
            }

            // 5. Scan Runtime Dependencies (40% -> 60%)
            for (int i = 0; i < runtimeFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = runtimeFiles[i];
                VerifySingleEntry(entry, tamperedFiles, missingFiles, accessDeniedFiles, diagnostics);
                totalScanned++;

                int pct = 40 + (int)Math.Round((i + 1.0) / Math.Max(1, runtimeFiles.Count) * 20);
                progress?.Report(new IntegrityScanProgress(
                    IntegrityScanCategory.RuntimeDependencies,
                    TotalItems: manifest.Files.Count,
                    ProcessedItems: totalScanned,
                    OverallProgressPercentage: Math.Min(60, pct),
                    CurrentItemName: entry.RelativePath));
            }

            // 6. Scan Resources (60% -> 70%)
            for (int i = 0; i < resourceFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = resourceFiles[i];
                VerifySingleEntry(entry, tamperedFiles, missingFiles, accessDeniedFiles, diagnostics);
                totalScanned++;

                int pct = 60 + (int)Math.Round((i + 1.0) / Math.Max(1, resourceFiles.Count) * 10);
                progress?.Report(new IntegrityScanProgress(
                    IntegrityScanCategory.Resources,
                    TotalItems: manifest.Files.Count,
                    ProcessedItems: totalScanned,
                    OverallProgressPercentage: Math.Min(70, pct),
                    CurrentItemName: entry.RelativePath));
            }

            // 7. Verify Manifest HMAC Signature (70% -> 80%)
            progress?.Report(new IntegrityScanProgress(
                IntegrityScanCategory.ManifestVerification,
                TotalItems: manifest.Files.Count,
                ProcessedItems: totalScanned,
                OverallProgressPercentage: 75,
                CurrentItemName: "Verifying security manifest signature..."));

            var canonicalContent = BuildCanonicalManifestString(manifest.Files);
            var expectedSignature = ComputeSignature(canonicalContent);

            if (!string.Equals(manifest.Signature, expectedSignature, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[INTEGRITY ERROR] Application integrity manifest signature verification failed. Manifest has been altered.");
                return new BinaryIntegrityResult(
                    false,
                    "Application integrity manifest signature is invalid.",
                    new[] { ManifestFileName },
                    Array.Empty<string>(),
                    DateTime.UtcNow,
                    totalScanned,
                    IntegrityFailureType.SignatureInvalid,
                    diagnostics,
                    CurrentApplicationVersion,
                    manifest.ApplicationVersion);
            }

            if (accessDeniedFiles.Count > 0)
            {
                var message = $"Access denied to {accessDeniedFiles.Count} critical application file(s).";
                _logger.LogError("[INTEGRITY ACCESS DENIED] {Message} Files: [{List}]", message, string.Join(", ", accessDeniedFiles));
                return new BinaryIntegrityResult(
                    false,
                    message,
                    Array.Empty<string>(),
                    accessDeniedFiles,
                    DateTime.UtcNow,
                    totalScanned,
                    IntegrityFailureType.AccessDenied,
                    diagnostics,
                    CurrentApplicationVersion,
                    manifest.ApplicationVersion);
            }

            if (missingFiles.Count > 0)
            {
                var message = $"Missing {missingFiles.Count} critical application file(s).";
                _logger.LogError("[INTEGRITY MISSING] {Message} Missing: [{MissingList}]", message, string.Join(", ", missingFiles));
                return new BinaryIntegrityResult(
                    false,
                    message,
                    tamperedFiles,
                    missingFiles,
                    DateTime.UtcNow,
                    totalScanned,
                    IntegrityFailureType.FileMissing,
                    diagnostics,
                    CurrentApplicationVersion,
                    manifest.ApplicationVersion);
            }

            if (tamperedFiles.Count > 0)
            {
                var message = $"{tamperedFiles.Count} critical application file(s) have been altered.";
                _logger.LogError("[INTEGRITY TAMPERED] {Message} Tampered: [{TamperedList}]", message, string.Join(", ", tamperedFiles));
                return new BinaryIntegrityResult(
                    false,
                    message,
                    tamperedFiles,
                    missingFiles,
                    DateTime.UtcNow,
                    totalScanned,
                    IntegrityFailureType.FileModified,
                    diagnostics,
                    CurrentApplicationVersion,
                    manifest.ApplicationVersion);
            }

            progress?.Report(new IntegrityScanProgress(
                IntegrityScanCategory.Completed,
                TotalItems: manifest.Files.Count,
                ProcessedItems: totalScanned,
                OverallProgressPercentage: 80,
                CurrentItemName: "Integrity scan completed."));

            _logger.LogInformation("[INTEGRITY PASS] All {Count} critical application files verified successfully.", manifest.Files.Count);
            return new BinaryIntegrityResult(
                true,
                "Application integrity verified successfully.",
                Array.Empty<string>(),
                Array.Empty<string>(),
                DateTime.UtcNow,
                totalScanned,
                IntegrityFailureType.None,
                diagnostics,
                CurrentApplicationVersion,
                manifest.ApplicationVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[INTEGRITY ERROR] Unexpected error during integrity verification.");
            return new BinaryIntegrityResult(
                false,
                $"Integrity check error: {ex.Message}",
                Array.Empty<string>(),
                Array.Empty<string>(),
                DateTime.UtcNow,
                0,
                IntegrityFailureType.UnexpectedLayout,
                diagnostics,
                CurrentApplicationVersion,
                "Unknown");
        }
    }

    private void VerifySingleEntry(
        IntegrityFileDto entry,
        List<string> tamperedFiles,
        List<string> missingFiles,
        List<string> accessDeniedFiles,
        List<IntegrityDiagnosticDetail> diagnostics)
    {
        var normalizedRelPath = entry.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var filePath = Path.Combine(_baseDirectory, normalizedRelPath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("[INTEGRITY MISMATCH] Missing critical application file: '{RelativePath}' at path: '{FullPath}'.",
                entry.RelativePath, filePath);
            missingFiles.Add(entry.RelativePath);
            diagnostics.Add(new IntegrityDiagnosticDetail(entry.RelativePath, entry.Sha256, string.Empty, entry.SizeBytes, 0, "Missing"));
            return;
        }

        try
        {
            var actualSha256 = ComputeFileSha256(filePath);
            var actualSize = new FileInfo(filePath).Length;

            if (!string.Equals(actualSha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[INTEGRITY MISMATCH] Binary hash mismatch for '{RelativePath}'. Expected SHA256='{Expected}' (Size: {ExpSize}), Actual SHA256='{Actual}' (Size: {ActSize}).",
                    entry.RelativePath, entry.Sha256, entry.SizeBytes, actualSha256, actualSize);
                tamperedFiles.Add(entry.RelativePath);
                diagnostics.Add(new IntegrityDiagnosticDetail(entry.RelativePath, entry.Sha256, actualSha256, entry.SizeBytes, actualSize, "Modified"));
            }
            else
            {
                _logger.LogDebug("[INTEGRITY VERIFIED] File '{RelativePath}' passed integrity check.", entry.RelativePath);
                diagnostics.Add(new IntegrityDiagnosticDetail(entry.RelativePath, entry.Sha256, actualSha256, entry.SizeBytes, actualSize, "Verified"));
            }
        }
        catch (UnauthorizedAccessException uex)
        {
            _logger.LogError(uex, "[INTEGRITY ACCESS DENIED] Unauthorized access reading file: '{RelativePath}'.", entry.RelativePath);
            accessDeniedFiles.Add(entry.RelativePath);
            diagnostics.Add(new IntegrityDiagnosticDetail(entry.RelativePath, entry.Sha256, string.Empty, entry.SizeBytes, 0, "AccessDenied"));
        }
        catch (IOException ioEx)
        {
            _logger.LogError(ioEx, "[INTEGRITY IO ERROR] IO error reading file: '{RelativePath}'.", entry.RelativePath);
            accessDeniedFiles.Add(entry.RelativePath);
            diagnostics.Add(new IntegrityDiagnosticDetail(entry.RelativePath, entry.Sha256, string.Empty, entry.SizeBytes, 0, "Locked"));
        }
    }

    public static (bool Success, IntegrityManifestDto? Manifest, string Error) ParseManifestJson(string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return (false, null, "Manifest JSON content is null or whitespace.");
        }

        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            var root = doc.RootElement;

            string appVersion = CurrentApplicationVersion;
            if (root.TryGetProperty("ApplicationVersion", out var avProp) || root.TryGetProperty("applicationVersion", out avProp))
            {
                appVersion = avProp.GetString() ?? CurrentApplicationVersion;
            }

            string signature = string.Empty;
            if (root.TryGetProperty("Signature", out var sigProp) || root.TryGetProperty("signature", out sigProp))
            {
                signature = sigProp.GetString() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                return (false, null, "Manifest signature property is missing or empty.");
            }

            JsonElement filesElement = default;
            if (!root.TryGetProperty("Files", out filesElement) && !root.TryGetProperty("files", out filesElement))
            {
                return (false, null, "Manifest files array is missing.");
            }

            if (filesElement.ValueKind != JsonValueKind.Array)
            {
                return (false, null, "Manifest files element is not a JSON array.");
            }

            var files = new List<IntegrityFileDto>();
            foreach (var item in filesElement.EnumerateArray())
            {
                string relPath = string.Empty;
                if (item.TryGetProperty("RelativePath", out var rp) || item.TryGetProperty("relativePath", out rp) || item.TryGetProperty("relative_path", out rp))
                {
                    relPath = rp.GetString() ?? string.Empty;
                }

                string sha = string.Empty;
                if (item.TryGetProperty("Sha256", out var sp) || item.TryGetProperty("sha256", out sp) || item.TryGetProperty("sha_256", out sp))
                {
                    sha = sp.GetString() ?? string.Empty;
                }

                long size = 0;
                if (item.TryGetProperty("SizeBytes", out var sz) || item.TryGetProperty("sizeBytes", out sz) || item.TryGetProperty("size_bytes", out sz) || item.TryGetProperty("size", out sz))
                {
                    if (sz.ValueKind == JsonValueKind.Number)
                    {
                        size = sz.GetInt64();
                    }
                }

                if (!string.IsNullOrWhiteSpace(relPath) && !string.IsNullOrWhiteSpace(sha))
                {
                    relPath = relPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    files.Add(new IntegrityFileDto
                    {
                        RelativePath = relPath,
                        Sha256 = sha.Trim().ToUpperInvariant(),
                        SizeBytes = size
                    });
                }
            }

            if (files.Count == 0)
            {
                return (false, null, "Manifest contains zero valid file records.");
            }

            return (true, new IntegrityManifestDto
            {
                ApplicationVersion = appVersion,
                GeneratedAtUtc = DateTime.UtcNow,
                Files = files,
                Signature = signature.Trim().ToUpperInvariant()
            }, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, null, $"JSON parse exception: {ex.Message}");
        }
    }

    private async Task<BinaryIntegrityResult> ScanDevelopmentModeAsync(
        IProgress<IntegrityScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var criticalPatterns = new[]
        {
            "DhirDhar.Application.dll",
            "DhirDhar.Domain.dll",
            "DhirDhar.Infrastructure.dll",
            "appsettings.json"
        };

        int total = criticalPatterns.Length;
        int current = 0;
        var diagnostics = new List<IntegrityDiagnosticDetail>();

        foreach (var file in criticalPatterns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            var filePath = Path.Combine(_baseDirectory, file);
            var exists = File.Exists(filePath);
            _logger.LogDebug("[INTEGRITY DEV] Checking presence of '{File}': {Exists}", file, exists);

            diagnostics.Add(new IntegrityDiagnosticDetail(
                file,
                string.Empty,
                exists ? ComputeFileSha256(filePath) : string.Empty,
                0,
                exists ? new FileInfo(filePath).Length : 0,
                exists ? "Verified" : "Missing"));

            int pct = 10 + (int)Math.Round((double)current / total * 70);
            progress?.Report(new IntegrityScanProgress(
                IntegrityScanCategory.ApplicationBinaries,
                TotalItems: total,
                ProcessedItems: current,
                OverallProgressPercentage: pct,
                CurrentItemName: file));

            await Task.Yield();
        }

        progress?.Report(new IntegrityScanProgress(
            IntegrityScanCategory.Completed,
            TotalItems: total,
            ProcessedItems: total,
            OverallProgressPercentage: 80,
            CurrentItemName: "Dev verification complete."));

        return new BinaryIntegrityResult(
            true,
            "Development mode: Assemblies verified.",
            Array.Empty<string>(),
            Array.Empty<string>(),
            DateTime.UtcNow,
            total,
            IntegrityFailureType.None,
            diagnostics,
            CurrentApplicationVersion,
            "DevMode");
    }

    public string GenerateIntegrityManifest(string baseDirectory)
    {
        var criticalFilesList = new List<string>();

        // Core Executables & Assemblies
        var searchPatterns = new[]
        {
            "DhirDhar.Desktop.exe",
            "DhirDhar.Application.dll",
            "DhirDhar.Domain.dll",
            "DhirDhar.Infrastructure.dll",
            "DhirDhar.Desktop.dll",
            "DhirDharUpdater.exe",
            "DhirDharUpdater.dll",
            "appsettings.json",
            "DhirDhar.Desktop.runtimeconfig.json",
            "DhirDhar.Desktop.deps.json",
            "DhirDharUpdater.deps.json",
            "resources.pri",
            "coreclr.dll",
            "clrjit.dll",
            "Microsoft.ui.xaml.dll",
            "Microsoft.WinUI.dll",
            "Microsoft.WindowsAppRuntime.Bootstrap.dll"
        };

        foreach (var pattern in searchPatterns)
        {
            var matches = Directory.GetFiles(baseDirectory, pattern, SearchOption.TopDirectoryOnly);
            foreach (var match in matches)
            {
                if (!criticalFilesList.Contains(match, StringComparer.OrdinalIgnoreCase))
                {
                    criticalFilesList.Add(match);
                }
            }
        }

        var files = new List<IntegrityFileDto>();

        foreach (var file in criticalFilesList)
        {
            var relativePath = Path.GetRelativePath(baseDirectory, file);
            var sha256 = ComputeFileSha256(file);
            var size = new FileInfo(file).Length;

            files.Add(new IntegrityFileDto
            {
                RelativePath = relativePath,
                Sha256 = sha256,
                SizeBytes = size
            });
        }

        files = files.OrderBy(f => f.RelativePath.ToUpperInvariant(), StringComparer.Ordinal).ToList();

        var canonical = BuildCanonicalManifestString(files);
        var signature = ComputeSignature(canonical);

        var manifest = new IntegrityManifestDto
        {
            ApplicationVersion = CurrentApplicationVersion,
            GeneratedAtUtc = DateTime.UtcNow,
            Files = files,
            Signature = signature
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(manifest, options);

        var outputPath = Path.Combine(baseDirectory, ManifestFileName);
        File.WriteAllText(outputPath, json, Encoding.UTF8);

        _logger?.LogInformation("[INTEGRITY] Generated integrity manifest '{ManifestFile}' with {Count} critical files.", ManifestFileName, files.Count);
        return outputPath;
    }

    public static string ComputeFileSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    public static string ComputeSignature(string content)
    {
        using var hmac = new HMACSHA256(IntegrityHmacKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash);
    }

    public static string BuildCanonicalManifestString(IEnumerable<IntegrityFileDto> files)
    {
        var sb = new StringBuilder();
        foreach (var f in files.OrderBy(x => x.RelativePath.ToUpperInvariant(), StringComparer.Ordinal))
        {
            var normalizedRelPath = f.RelativePath.Replace('/', '\\').ToUpperInvariant();
            sb.Append(normalizedRelPath)
              .Append('|')
              .Append(f.Sha256.ToUpperInvariant())
              .Append('|')
              .Append(f.SizeBytes)
              .Append('\n');
        }
        return sb.ToString();
    }
}
