using System;

namespace DhirDhar.Application.Validation;

public interface IIdempotencyService
{
    bool TryAcquireLock(string idempotencyKey, TimeSpan? duration = null);

    void ReleaseLock(string idempotencyKey);

    bool IsDuplicateSubmission(string idempotencyKey);

    void RegisterCompleted(string idempotencyKey);
}
