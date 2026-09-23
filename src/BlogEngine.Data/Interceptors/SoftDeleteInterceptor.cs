using BlogEngine.Data.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlogEngine.Data.Interceptors;

/// <summary>
/// Rewrites hard deletes of <see cref="ISoftDeletable"/> entities into flag updates.
/// </summary>
/// <remarks>
/// This is a safety net, not the intended path: services expose explicit trash/restore
/// operations. It exists so that a stray <c>Remove</c> call cannot destroy a row that other
/// history still references.
/// <para>
/// An entity that is already in the trash (its original <see cref="ISoftDeletable.IsDeleted"/> is
/// <see langword="true"/>) is let through as a real delete, which is how "empty trash" works.
/// </para>
/// <para>
/// This must run before <see cref="FingerprintInterceptor"/> and <see cref="AuditInterceptor"/> so the
/// rewritten row is stamped as modified and audited as an update rather than a delete.
/// </para>
/// </remarks>
public sealed class SoftDeleteInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplySoftDeletes(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ApplySoftDeletes(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Turns each pending delete of a live soft-deletable entity into an update of its trash flag.
    /// </summary>
    private void ApplySoftDeletes(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Materialise first: changing entry state while enumerating the change tracker is unsafe.
        var deleted = context.ChangeTracker.Entries<ISoftDeletable>()
            .Where(e => e.State == EntityState.Deleted)
            .ToList();

        var now = timeProvider.GetUtcNow();

        foreach (var entry in deleted)
        {
            var isDeleted = entry.Property<bool>(nameof(ISoftDeletable.IsDeleted));
            if (isDeleted.OriginalValue)
            {
                continue;
            }

            // Unchanged first, then set values, so only the trash columns are marked modified
            // instead of every column (which is what setting State = Modified would do).
            entry.State = EntityState.Unchanged;
            isDeleted.CurrentValue = true;
            entry.Property<DateTimeOffset?>(nameof(ISoftDeletable.DeletedOn)).CurrentValue = now;
        }
    }
}