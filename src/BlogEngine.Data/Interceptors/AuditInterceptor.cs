using BlogEngine.Data.Interfaces;
using BlogEngine.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlogEngine.Data.Interceptors;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every entity added, modified or deleted in a save.
/// </summary>
/// <remarks>
/// The audit rows are added to the same change set, so they commit or roll back together with the
/// changes they describe. This must be the last save interceptor so it sees the final state produced
/// by <see cref="SoftDeleteInterceptor"/> and <see cref="FingerprintInterceptor"/>.
/// </remarks>
public sealed class AuditInterceptor(IUserService userService) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddAuditLogs(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AddAuditLogs(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Builds one audit entry per changed entity and adds the resulting logs to the context.
    /// </summary>
    private void AddAuditLogs(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Earlier interceptors may have changed CLR values; make sure the tracker has seen them.
        context.ChangeTracker.DetectChanges();

        var userId = userService.UserId;
        var auditEntries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => CreateAuditEntry(e, userId))
            .ToList();

        context.Set<AuditLog>().AddRange(auditEntries.Select(e => e.ToAuditLog()));
    }

    /// <summary>
    /// Captures the key and the old/new values of an entity according to its pending state.
    /// </summary>
    private static AuditEntry CreateAuditEntry(EntityEntry entry, int userId)
    {
        var auditEntry = new AuditEntry(entry)
        {
            TableName = entry.Entity.GetType().Name,
            UserId = userId
        };

        foreach (var property in entry.Properties)
        {
            var propertyName = property.Metadata.Name;
            if (property.Metadata.IsPrimaryKey())
            {
                auditEntry.KeyValues[propertyName] = property.CurrentValue!;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    auditEntry.AuditType = AuditType.Create;
                    auditEntry.NewValues[propertyName] = property.CurrentValue!;
                    break;
                case EntityState.Deleted:
                    auditEntry.AuditType = AuditType.Delete;
                    auditEntry.OldValues[propertyName] = property.OriginalValue!;
                    break;
                case EntityState.Modified when property.IsModified:
                    auditEntry.ChangedColumns.Add(propertyName);
                    auditEntry.AuditType = AuditType.Update;
                    auditEntry.OldValues[propertyName] = property.OriginalValue!;
                    auditEntry.NewValues[propertyName] = property.CurrentValue!;
                    break;
            }
        }

        return auditEntry;
    }
}