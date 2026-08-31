using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DhirDhar.Application.Licensing;
using Microsoft.Win32;

namespace DhirDhar.Infrastructure.Licensing;

public sealed class DeviceFingerprintService : IDeviceFingerprintService
{
    private string? _cachedFingerprint;

    public string GetDeviceFingerprint()
    {
        if (!string.IsNullOrEmpty(_cachedFingerprint))
        {
            return _cachedFingerprint;
        }

        var machineGuid = GetMachineGuid();
        var cpuId = GetProcessorIdentifier();
        var motherboardId = GetMotherboardIdentifier();
        var systemDriveSerial = GetSystemDriveSerial();

        // Salt and combine hardware identifiers
        var combined = $"DhirDhar::PC::{machineGuid}::{cpuId}::{motherboardId}::{systemDriveSerial}";
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));

        var hex = Convert.ToHexString(hashBytes).ToUpperInvariant();
        // Format as DD-PC-XXXX-XXXX-XXXX-XXXX
        _cachedFingerprint = $"DD-PC-{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}";
        return _cachedFingerprint;
    }

    public bool ValidateDeviceFingerprint(string expectedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            return false;
        }

        var currentFingerprint = GetDeviceFingerprint();
        return string.Equals(currentFingerprint.Trim(), expectedFingerprint.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMachineGuid()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    return guid.Trim();
                }
            }
        }
        catch
        {
        }

        return Environment.MachineName;
    }

    private static string GetProcessorIdentifier()
    {
        try
        {
            var proc = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty;
            var arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty;
            var count = Environment.ProcessorCount.ToString();
            return $"{proc}::{arch}::{count}".Trim();
        }
        catch
        {
            return Environment.ProcessorCount.ToString();
        }
    }

    private static string GetMotherboardIdentifier()
    {
        // 1. Primary: Windows Registry BIOS/Motherboard hardware info (non-privileged, fast, zero-WMI)
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (biosKey != null)
                {
                    var baseBoardProduct = biosKey.GetValue("BaseBoardProduct")?.ToString();
                    var baseBoardManufacturer = biosKey.GetValue("BaseBoardManufacturer")?.ToString();
                    var systemProductName = biosKey.GetValue("SystemProductName")?.ToString();
                    var biosSerial = biosKey.GetValue("BIOSSerialNumber")?.ToString();

                    var combined = $"{baseBoardManufacturer}_{baseBoardProduct}_{systemProductName}_{biosSerial}".Trim('_');
                    if (!string.IsNullOrWhiteSpace(combined) && combined != "None_None_None_None" && combined != "Default string")
                    {
                        return combined;
                    }
                }
            }
        }
        catch
        {
        }

        // 2. Secondary: WMI query with safe error containment and diagnostic logging
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var searcher = new ManagementObjectSearcher(
                    new ManagementScope(@"\\.\root\cimv2"),
                    new ObjectQuery("SELECT SerialNumber, UUID FROM Win32_ComputerSystemProduct"),
                    new System.Management.EnumerationOptions { ReturnImmediately = true, Timeout = TimeSpan.FromSeconds(2) });

                using var collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        var uuid = obj["UUID"]?.ToString();
                        var serial = obj["SerialNumber"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(uuid) && uuid != "00000000-0000-0000-0000-000000000000")
                        {
                            return $"{uuid}_{serial}";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] WMI query handled gracefully: {ex.Message}");
        }

        // 3. Fallback: Stable fallback
        return "SYS-BASE-00";
    }

    private static string GetSystemDriveSerial()
    {
        try
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var driveInfo = new DriveInfo(systemDrive);
            return $"{driveInfo.VolumeLabel}_{driveInfo.TotalSize}";
        }
        catch
        {
            return "VOL-SYS-00";
        }
    }
}
