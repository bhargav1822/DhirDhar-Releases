using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Validation.Models;

namespace DhirDhar.Application.Validation;

public interface IIntegrityService
{
    Task<IntegrityScanReport> RunIntegrityScanAsync(CancellationToken cancellationToken = default);

    Task<FinancialValidationResult> ValidateImportPayloadAsync(string rawPayload, CancellationToken cancellationToken = default);

    Task<FinancialValidationResult> ValidateRestorePackageAsync(string backupPath, CancellationToken cancellationToken = default);
    Task<FinancialValidationResult> RepairIssueAsync(string repairActionKey, string entityId, CancellationToken cancellationToken = default);
}
