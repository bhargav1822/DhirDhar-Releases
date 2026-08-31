using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DhirDhar.Infrastructure.Licensing;

namespace DhirDhar.LicenseGenerator;

public sealed record LicenseHistoryRecord(
    string LicenseId,
    string IssuanceId,
    string CustomerName,
    string CustomerEmail,
    string Edition,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    int DeviceLimit,
    string? DeviceBinding,
    string? PreviousLicenseId,
    bool IsRenewal,
    string SerialKey,
    DateTime CreatedAt);

public sealed class LicenseHistoryService
{
    private readonly string _historyFilePath;
    private readonly object _lock = new();
    private List<LicenseHistoryRecord> _records = new();

    public LicenseHistoryService(string? customHistoryPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customHistoryPath))
        {
            _historyFilePath = customHistoryPath;
        }
        else
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar", "LicenseGenerator");
            Directory.CreateDirectory(dir);
            _historyFilePath = Path.Combine(dir, "license_history.json");
        }

        LicenseDecoder.RegisterHistoryPath(_historyFilePath);
        LoadHistory();
    }

    public IReadOnlyList<LicenseHistoryRecord> GetAllRecords()
    {
        lock (_lock)
        {
            return _records.ToList().AsReadOnly();
        }
    }

    public bool Exists(string licenseId, string issuanceId, string serialKey)
    {
        lock (_lock)
        {
            return _records.Any(r =>
                string.Equals(r.LicenseId, licenseId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.IssuanceId, issuanceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.SerialKey, serialKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void AddRecord(LicenseHistoryRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
            LicenseDecoder.RegisterKnownCustomer(record.SerialKey, record.LicenseId, record.CustomerName, record.CustomerEmail);
            SaveHistory();
        }
    }

    public LicenseHistoryRecord? FindByLicenseId(string licenseId)
    {
        lock (_lock)
        {
            return _records.LastOrDefault(r => string.Equals(r.LicenseId, licenseId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void PrintHistory()
    {
        lock (_lock)
        {
            if (_records.Count == 0)
            {
                Console.WriteLine("No license history records found.");
                return;
            }

            Console.WriteLine("==========================================================================================================");
            Console.WriteLine("                                       DHIRDHAR LICENSE HISTORY                                           ");
            Console.WriteLine("==========================================================================================================");
            Console.WriteLine(string.Format("{0,-20} | {1,-10} | {2,-20} | {3,-12} | {4,-12} | {5,-16}",
                "License ID", "Type", "Customer", "Issue Date", "Expiry Date", "Previous Lic ID"));
            Console.WriteLine("----------------------------------------------------------------------------------------------------------");

            foreach (var r in _records)
            {
                var type = r.IsRenewal ? "Renewal" : r.Edition;
                var prevId = string.IsNullOrEmpty(r.PreviousLicenseId) ? "-" : r.PreviousLicenseId;
                Console.WriteLine(string.Format("{0,-20} | {1,-10} | {2,-20} | {3,-12:dd-MMM-yyyy} | {4,-12:dd-MMM-yyyy} | {5,-16}",
                    r.LicenseId,
                    type,
                    r.CustomerName.Length > 20 ? r.CustomerName.Substring(0, 17) + "..." : r.CustomerName,
                    r.IssuedAt,
                    r.ExpiresAt,
                    prevId));
            }

            Console.WriteLine("==========================================================================================================");
            Console.WriteLine($"Total Records: {_records.Count}");
            Console.WriteLine();
        }
    }

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var records = JsonSerializer.Deserialize<List<LicenseHistoryRecord>>(json);
                if (records != null)
                {
                    _records = records;
                    foreach (var r in _records)
                    {
                        LicenseDecoder.RegisterKnownCustomer(r.SerialKey, r.LicenseId, r.CustomerName, r.CustomerEmail);
                    }
                }
            }
        }
        catch
        {
            _records = new List<LicenseHistoryRecord>();
        }
    }

    private void SaveHistory()
    {
        try
        {
            var dir = Path.GetDirectoryName(_historyFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_records, options);
            File.WriteAllText(_historyFilePath, json);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[WARNING] Failed to save license history: {ex.Message}");
            Console.ResetColor();
        }
    }
}
