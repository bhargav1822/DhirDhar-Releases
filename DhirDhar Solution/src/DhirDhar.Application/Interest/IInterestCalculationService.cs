using DhirDhar.Domain.Interest;

namespace DhirDhar.Application.Interest;

public interface IInterestCalculationService
{
    Task<InterestCalculationResult> CalculateAsync(
        Guid borrowerId,
        DateTime requestedEndDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, decimal>> CalculateBatchAsync(
        IReadOnlyList<Guid> borrowerIds,
        DateTime requestedEndDate,
        CancellationToken cancellationToken = default);
}
