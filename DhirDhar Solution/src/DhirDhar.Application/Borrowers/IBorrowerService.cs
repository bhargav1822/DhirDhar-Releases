namespace DhirDhar.Application.Borrowers;

public interface IBorrowerService
{
    Task<string> GetNextBorrowerNumberAsync(CancellationToken cancellationToken = default);

    Task<string> GetBorrowerPrefixAsync(CancellationToken cancellationToken = default);

    Task<Borrowers.Models.BorrowerSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Borrowers.Models.BorrowerSummary?> GetByBorrowerNumberAsync(string borrowerNumber, CancellationToken cancellationToken = default);

    Task<Borrowers.Models.BorrowerListResult> GetListAsync(
        Borrowers.Models.BorrowerFilter filter,
        string? searchTerm = null,
        int page = 1,
        int pageSize = 0,
        CancellationToken cancellationToken = default);

    Task<Borrowers.Models.BorrowerSummary> CreateAsync(Borrowers.Models.CreateBorrowerRequest request, CancellationToken cancellationToken = default);

    Task<Borrowers.Models.BorrowerSummary> UpdateAsync(Borrowers.Models.UpdateBorrowerRequest request, CancellationToken cancellationToken = default);

    Task<Borrowers.Models.BorrowerSummary> ChangeStatusAsync(Guid id, Domain.Enums.BorrowerStatus status, CancellationToken cancellationToken = default);

    Task CloseAccountAsync(Guid borrowerId, DateTime closedDate, decimal? closingAmount = null, decimal? closingInterest = null, CancellationToken cancellationToken = default);
}
