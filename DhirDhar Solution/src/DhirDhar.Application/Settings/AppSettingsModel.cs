using DhirDhar.Domain.Common;

namespace DhirDhar.Application.Settings;

public sealed class AppSettingsModel
{
    public string Language { get; set; } = "en-US";
    public string DateFormat { get; set; } = "DD-MM-YYYY";
    public string Currency { get; set; } = "INR";
    public string Theme { get; set; } = "Default";
    public bool UpdatesAutoCheckEnabled { get; set; } = true;
    public bool UpdatesAutoInstallEnabled { get; set; } = true;

    public bool AutomaticBackupEnabled { get; set; } = true;
    public string BackupFrequency { get; set; } = "Daily";
    public int RetentionCount { get; set; } = 7;
    public DateTime? LastAutomaticBackupTime { get; set; }
    public DateTime? NextScheduledBackupTime { get; set; }

    public string BusinessName { get; set; } = BusinessProfileHelper.DefaultBusinessName;

    public string BorrowerNumberPrefix => BusinessProfileHelper.GeneratePrefix(BusinessName);

    // Printing & POS Thermal Paper Settings
    public string PaperSize { get; set; } = "A4";
    public double CustomPaperWidthMm { get; set; } = 80.0;
    public bool AutoCutPaper { get; set; } = true;
    public string? SelectedPrinter { get; set; }
}
