using System;
using System.Collections.Generic;

namespace DhirDhar.Application.Validation.Models;

public enum IntegritySeverityLevel
{
    Info = 0,
    Warning = 1,
    High = 2,
    Critical = 3
}

public enum IntegrityStatus
{
    Pass = 0,
    Warning = 1,
    High = 2,
    Critical = 3
}

public sealed record FinancialValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static FinancialValidationResult Success() =>
        new(true, Array.Empty<string>(), Array.Empty<string>());

    public static FinancialValidationResult Failure(IEnumerable<string> errors) =>
        new(false, new List<string>(errors), Array.Empty<string>());

    public static FinancialValidationResult Failure(string error) =>
        new(false, new[] { error }, Array.Empty<string>());
}

public sealed record IntegrityIssue(
    string Category,
    IntegritySeverityLevel Severity,
    string EntityName,
    string EntityId,
    string Description,
    string FailureCode,
    string? TechnicalDetails = null,
    string? RecoveryHint = null,
    string? Title = null,
    string? BorrowerNumber = null,
    bool IsRepairable = false,
    string? RepairActionKey = null);

public sealed record IntegrityCategoryReport(
    string CategoryName,
    IntegrityStatus Status,
    int TotalChecked,
    int IssueCount,
    IReadOnlyList<IntegrityIssue> Issues);

public sealed record IntegrityScanReport(
    IntegrityStatus OverallStatus,
    int TotalBorrowersChecked,
    int TotalTransactionsChecked,
    int TotalLedgerEntriesChecked,
    int TotalIssuesFound,
    IReadOnlyList<IntegrityCategoryReport> Categories,
    DateTime ScannedAt,
    TimeSpan ExecutionTime);
