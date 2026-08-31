using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels;

namespace DhirDhar.Desktop.Services;

public interface IApplicationStartupService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task InitializeAsync(IProgress<StartupProgress>? progress, CancellationToken cancellationToken = default);
}
