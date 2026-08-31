using DhirDhar.Application.Ledger.Models;

namespace DhirDhar.Application.Ledger;

public interface ILedgerService
{
    Task<LedgerSummary> GetSummaryAsync(Guid borrowerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LedgerEntryDto>> GetEntriesAsync(
        Guid borrowerId,
        DateTime? startDate,
        DateTime? endDate,
        string? eventTypeFilter,
        string? searchTerm,
        CancellationToken cancellationToken = default);
}
