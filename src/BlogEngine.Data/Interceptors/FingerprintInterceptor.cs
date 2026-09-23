using BlogEngine.Data.Interfaces;
using BlogEngine.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlogEngine.Data.Interceptors;

/// <summary>
/// Stamps <see cref="FingerPrintEntityBase"/> entities with who created and last modified them, and when.
/// </summary>
/// <remarks>
/// Values are written through the change tracker rather than the CLR properties so the columns are
/// flagged as modified immediately, without depending on a later <c>DetectChanges</c> call.
/// </remarks>
public sealed class FingerprintInterceptor(IUserService userService, TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyFingerprints(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ApplyFingerprints(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Sets the created and modified stamps on added entities and the modified stamps on updated ones.
    /// </summary>
    private void ApplyFingerprints(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var userId = userService.UserId;

        foreach (var entry in context.ChangeTracker.Entries<FingerPrintEntityBase>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(e => e.CreatedBy).CurrentValue = userId;
                    entry.Property(e => e.CreatedOn).CurrentValue = now;
                    entry.Property(e => e.ModifiedBy).CurrentValue = userId;
                    entry.Property(e => e.ModifiedOn).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(e => e.ModifiedBy).CurrentValue = userId;
                    entry.Property(e => e.ModifiedOn).CurrentValue = now;
                    break;
            }
        }
    }
}