using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Profiles;

/// <summary>
/// Provides read/write access to the created user's/profile's display name.
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Returns the stored profile name, or <c>null</c> when no profile name has been set.
    /// </summary>
    Task<string?> GetProfileNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the profile name, replacing any previously stored value.
    /// </summary>
    Task SetProfileNameAsync(string name, CancellationToken cancellationToken = default);
}
