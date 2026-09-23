namespace BlogEngine.Data.Interfaces;

/// <summary>
/// Marks an entity whose deletes are rewritten into a trash flag by
/// <see cref="Interceptors.SoftDeleteInterceptor"/>.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>Whether the entity is in the trash.</summary>
    bool IsDeleted { get; set; }

    /// <summary>When the entity was moved to the trash, or <see langword="null"/> if it never was.</summary>
    DateTimeOffset? DeletedOn { get; set; }
}