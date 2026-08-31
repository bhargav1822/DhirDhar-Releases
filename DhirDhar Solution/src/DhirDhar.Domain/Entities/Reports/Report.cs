using System;

namespace DhirDhar.Domain.Entities;

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReportName { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public DateTime GeneratedDate { get; set; }
    public string FilePath { get; set; } = string.Empty;
}
