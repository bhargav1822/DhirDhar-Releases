using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Security.Models;

namespace DhirDhar.Application.Security;

public interface IEncryptionMigrationService
{
    Task<bool> IsMigrationRequiredAsync(CancellationToken cancellationToken = default);

    Task<MigrationResult> MigrateExistingDataAsync(CancellationToken cancellationToken = default);
}
