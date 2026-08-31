using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Audit;

public sealed class AuditService : IAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDhar.Infrastructure.Persistence.DhirDharDbContext>();

            var auditEntry = new Domain.Entities.AuditEntry(
                Guid.NewGuid(),
                DateTime.UtcNow,
                auditEvent.Action,
                auditEvent.EntityType,
                auditEvent.EntityId,
                auditEvent.Description,
                auditEvent.Result,
                auditEvent.BeforeValue,
                auditEvent.AfterValue);

            dbContext.AuditEntries.Add(auditEntry);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("AuditRecorded: {Action}, Entity={EntityType}, Result={Result}", auditEvent.Action, auditEvent.EntityType, auditEvent.Result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit event: {Action}", auditEvent.Action);
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> GetAuditHistoryAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? action,
        string? entityType,
        string? result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDhar.Infrastructure.Persistence.DhirDharDbContext>();

            var query = dbContext.AuditEntries.AsNoTracking().AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(a => a.Timestamp >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(a => a.Timestamp <= toDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(a => a.Action == action);
            }

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                query = query.Where(a => a.EntityType == entityType);
            }

            if (!string.IsNullOrWhiteSpace(result))
            {
                query = query.Where(a => a.Result == result);
            }

            var entries = await query
                .OrderByDescending(a => a.Timestamp)
                .Take(500)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return entries.Select(e => new AuditEntry(
                e.Id,
                e.Timestamp,
                e.Action,
                e.EntityType,
                e.EntityId,
                e.Description,
                e.Result,
                e.BeforeValue,
                e.AfterValue)).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve audit history.");
            return Array.Empty<AuditEntry>();
        }
    }
}
