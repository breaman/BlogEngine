using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Data.Common;

/// <summary>
/// Classifies SQL Server failures surfaced through <see cref="DbUpdateException"/>.
/// </summary>
public static class DbUpdateExceptionExtensions
{
    /// <summary>SQL Server error: cannot insert duplicate key row in object with unique index.</summary>
    private const int UniqueIndexViolation = 2601;

    /// <summary>SQL Server error: violation of a PRIMARY KEY or UNIQUE constraint.</summary>
    private const int UniqueConstraintViolation = 2627;

    /// <summary>
    /// Whether the save failed because a row would duplicate a unique index or key, which is how a lost race
    /// (two saves creating the same tag or slug) shows up.
    /// </summary>
    public static bool IsUniqueViolation(this DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation };
    }
}