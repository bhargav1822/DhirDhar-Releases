using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DhirDhar.Infrastructure.Localization;

/// <summary>
/// Safely reads and parses the installer-generated language configuration (language.json).
/// Searches multiple application and user data locations as well as Windows registry keys.
/// Operates completely independently from Windows system display, keyboard, and IME settings.
/// </summary>
public static class LanguageConfigurationReader
{
    public const string LanguageConfigFileName = "language.json";
    public const string SafeFallbackLanguage = "en-IN";

    private static string? _customBaseDirectory;

    /// <summary>
    /// Optional override for the base directory where language.json is located (useful for testing).
    /// </summary>
    public static string? CustomBaseDirectory
    {
        get => _customBaseDirectory;
        set => _customBaseDirectory = value;
    }

    /// <summary>
    /// Reads the installer-selected language from language.json or registry in priority order.
    /// Returns the normalized canonical language code (e.g. "gu-IN", "hi-IN", "en-IN"), or null if missing/invalid.
    /// </summary>
    public static string? ReadInstallerLanguage(string? baseDirectory = null)
    {
        try
        {
            // 1. If explicit directory provided, check it and return immediately (scoped read for test/isolated environments)
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                var explicitLang = TryReadFromFile(Path.Combine(baseDirectory, LanguageConfigFileName));
                return !string.IsNullOrWhiteSpace(explicitLang)
                    ? LocalizationService.NormalizeLanguageCode(explicitLang)
                    : null;
            }

            // 2. If testing custom directory is set, check it and do not fall through
            if (!string.IsNullOrWhiteSpace(_customBaseDirectory))
            {
                var customLang = TryReadFromFile(Path.Combine(_customBaseDirectory, LanguageConfigFileName));
                return !string.IsNullOrWhiteSpace(customLang)
                    ? LocalizationService.NormalizeLanguageCode(customLang)
                    : null;
            }

            // 3. Check AppContext.BaseDirectory
            var appBase = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(appBase))
            {
                var lang = TryReadFromFile(Path.Combine(appBase, LanguageConfigFileName));
                if (!string.IsNullOrWhiteSpace(lang))
                {
                    return LocalizationService.NormalizeLanguageCode(lang);
                }
            }

            // 4. Check Process Path directory
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var procDir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(procDir))
                {
                    var lang = TryReadFromFile(Path.Combine(procDir, LanguageConfigFileName));
                    if (!string.IsNullOrWhiteSpace(lang))
                    {
                        return LocalizationService.NormalizeLanguageCode(lang);
                    }
                }
            }

            // 5. Check AppDomain BaseDirectory
            var appDomainDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(appDomainDir))
            {
                var lang = TryReadFromFile(Path.Combine(appDomainDir, LanguageConfigFileName));
                if (!string.IsNullOrWhiteSpace(lang))
                {
                    return LocalizationService.NormalizeLanguageCode(lang);
                }
            }

            // 6. Check %LocalAppData%\DhirDhar Solution\language.json
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var localAppDir = Path.Combine(localAppData, "DhirDhar Solution");
                var lang = TryReadFromFile(Path.Combine(localAppDir, LanguageConfigFileName));
                if (!string.IsNullOrWhiteSpace(lang))
                {
                    return LocalizationService.NormalizeLanguageCode(lang);
                }
            }

            // 7. Check %AppData%\DhirDhar Solution\language.json (Roaming)
            var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(roamingAppData))
            {
                var roamingDir = Path.Combine(roamingAppData, "DhirDhar Solution");
                var lang = TryReadFromFile(Path.Combine(roamingDir, LanguageConfigFileName));
                if (!string.IsNullOrWhiteSpace(lang))
                {
                    return LocalizationService.NormalizeLanguageCode(lang);
                }
            }

            // 8. Check %ProgramData%\DhirDhar Solution\language.json
            var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(commonAppData))
            {
                var commonDir = Path.Combine(commonAppData, "DhirDhar Solution");
                var lang = TryReadFromFile(Path.Combine(commonDir, LanguageConfigFileName));
                if (!string.IsNullOrWhiteSpace(lang))
                {
                    return LocalizationService.NormalizeLanguageCode(lang);
                }
            }

            // 9. Check Windows Registry (HKCU / HKLM)
            if (OperatingSystem.IsWindows())
            {
                var regLang = TryReadFromRegistry();
                if (!string.IsNullOrWhiteSpace(regLang))
                {
                    return LocalizationService.NormalizeLanguageCode(regLang);
                }
            }
        }
        catch
        {
            // Fallback safely on any read, I/O, or JSON parsing error
        }

        return null;
    }

    private static string? TryReadFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("language", out var langProp))
            {
                var val = langProp.GetString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
            else if (doc.RootElement.TryGetProperty("Language", out var langPropPascal))
            {
                var val = langPropPascal.GetString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        catch
        {
        }
        return null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? TryReadFromRegistry()
    {
        try
        {
            using var cuKey = Registry.CurrentUser.OpenSubKey(@"Software\DhirDhar Solution");
            var valCu = cuKey?.GetValue("InstallerLanguage") as string;
            if (!string.IsNullOrWhiteSpace(valCu)) return valCu;

            using var lmKey = Registry.LocalMachine.OpenSubKey(@"Software\DhirDhar Solution");
            var valLm = lmKey?.GetValue("InstallerLanguage") as string;
            if (!string.IsNullOrWhiteSpace(valLm)) return valLm;
        }
        catch
        {
        }
        return null;
    }

    /// <summary>
    /// Writes the language configuration file (language.json) with the specified canonical language code.
    /// </summary>
    public static void WriteInstallerLanguage(string languageCode, string? baseDirectory = null)
    {
        try
        {
            var code = LocalizationService.NormalizeLanguageCode(languageCode);
            var dir = baseDirectory ?? _customBaseDirectory ?? AppContext.BaseDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var path = Path.Combine(dir, LanguageConfigFileName);
            var payload = new { language = code };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best effort write
        }
    }
}
