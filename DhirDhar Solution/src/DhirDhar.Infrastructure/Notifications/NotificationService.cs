using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly ConcurrentDictionary<Guid, AppNotification> _notifications = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastSentCache = new();
    private readonly ILogger<NotificationService> _logger;
    private static readonly TimeSpan MinimumRepeatInterval = TimeSpan.FromMinutes(15);

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendNotificationAsync(
        string title,
        string message,
        NotificationSeverity severity = NotificationSeverity.Info,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var dedupeKey = $"{category ?? "General"}:{title}:{message}";
        var now = DateTime.UtcNow;

        if (_lastSentCache.TryGetValue(dedupeKey, out var lastSent) && now - lastSent < MinimumRepeatInterval)
        {
            _logger.LogDebug("Notification suppressed (duplicate within repeat interval): '{Title}'", title);
            return Task.CompletedTask;
        }

        _lastSentCache[dedupeKey] = now;

        var notification = new AppNotification(
            Guid.NewGuid(),
            title,
            message,
            severity,
            now,
            IsRead: false,
            category);

        _notifications[notification.Id] = notification;
        _logger.LogInformation("Notification [{Severity}]: '{Title}' - {Message}", severity, title, message);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AppNotification>> GetUnreadNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var list = _notifications.Values
            .Where(n => !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<AppNotification>>(list);
    }

    public Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        if (_notifications.TryGetValue(notificationId, out var existing))
        {
            _notifications[notificationId] = existing with { IsRead = true };
        }

        return Task.CompletedTask;
    }
}
