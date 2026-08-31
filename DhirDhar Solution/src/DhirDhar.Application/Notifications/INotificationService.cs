using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Notifications;

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    High = 2,
    Critical = 3
}

public sealed record AppNotification(
    Guid Id,
    string Title,
    string Message,
    NotificationSeverity Severity,
    DateTime CreatedAt,
    bool IsRead,
    string? Category = null);

public interface INotificationService
{
    Task SendNotificationAsync(string title, string message, NotificationSeverity severity = NotificationSeverity.Info, string? category = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppNotification>> GetUnreadNotificationsAsync(CancellationToken cancellationToken = default);

    Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
}
