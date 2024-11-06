using BlogEngine.Data.Interfaces;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Data.Models;

public class ApplicationDbContext : IdentityDbContext<User, Role, int>
{
    private readonly IUserService? _userService;

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Post> Posts => Set<Post>();

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IUserService userService) :
        base(options)
    {
        _userService = userService;
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        AddFingerPrinting();
        AddLogging();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges()
    {
        AddFingerPrinting();
        AddLogging();
        return base.SaveChanges();
    }

    private void AddFingerPrinting()
    {
        var modified = ChangeTracker.Entries().Where(e => e.State == EntityState.Modified);
        var added = ChangeTracker.Entries().Where(e => e.State == EntityState.Added);
        var now = DateTime.UtcNow;

        foreach (var entry in added)
            if (entry.Entity is FingerPrintEntityBase fingerPrintEntry)
            {
                fingerPrintEntry.CreatedBy = _userService?.UserId ?? default;
                fingerPrintEntry.CreatedOn = now;
                fingerPrintEntry.ModifiedBy = _userService?.UserId ?? default;
                fingerPrintEntry.ModifiedOn = now;
            }

        foreach (var entry in modified)
            if (entry.Entity is FingerPrintEntityBase fingerPrintEntry)
            {
                fingerPrintEntry.ModifiedBy = _userService?.UserId ?? default;
                fingerPrintEntry.ModifiedOn = now;
            }
    }

    private void AddLogging()
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached ||
                entry.State == EntityState.Unchanged) continue;

            var auditEntry = new AuditEntry(entry)
            {
                TableName = entry.Entity.GetType().Name,
                UserId = _userService?.UserId ?? default
            };
            auditEntries.Add(auditEntry);
            foreach (var property in entry.Properties)
            {
                var propertyName = property.Metadata.Name;
                if (property.Metadata.IsPrimaryKey()) auditEntry.KeyValues[propertyName] = property.CurrentValue!;
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
                    case EntityState.Modified:
                        if (property.IsModified)
                        {
                            auditEntry.ChangedColumns.Add(propertyName);
                            auditEntry.AuditType = AuditType.Update;
                            auditEntry.OldValues[propertyName] = property.OriginalValue!;
                            auditEntry.NewValues[propertyName] = property.CurrentValue!;
                        }

                        break;
                    case EntityState.Detached:
                        break;
                    case EntityState.Unchanged:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        foreach (var auditEntry in auditEntries) AuditLogs.Add(auditEntry.ToAuditLog());
    }
}