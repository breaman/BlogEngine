using BlogEngine.Data.Common;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Data.Models;

/// <summary>
/// Identity-backed base context that holds the audit log and model-wide conventions.
/// </summary>
/// <remarks>
/// Fingerprinting, soft deletes and audit logging are not implemented here. They run as EF Core save
/// interceptors (see <see cref="Interceptors.InterceptorRegistrationExtensions"/>) so they cover every
/// <c>SaveChanges</c> overload and can depend on scoped services without the context taking them.
/// </remarks>
public abstract class AuthDbContext : IdentityDbContext<User, Role, int>
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected AuthDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Model-wide defaults so no column silently falls back to a lossy provider default.
        // Types that need different precision (tax rates) override these in their own
        // IEntityTypeConfiguration.
        configurationBuilder.Properties<decimal>().HaveColumnType(ColumnTypes.Money);
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType(ColumnTypes.Timestamp);
    }
}