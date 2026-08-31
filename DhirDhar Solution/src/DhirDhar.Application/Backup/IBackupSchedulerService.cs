using System;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Backup;

public interface IBackupSchedulerService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task TriggerBackupCheckAsync(CancellationToken cancellationToken = default);
    event EventHandler? ScheduledBackupCompleted;
}
