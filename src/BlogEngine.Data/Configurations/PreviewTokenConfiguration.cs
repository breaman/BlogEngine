using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="PreviewToken"/> (design 6.7).
/// </summary>
public sealed class PreviewTokenConfiguration : IEntityTypeConfiguration<PreviewToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PreviewToken> builder)
    {
        // base64url is ASCII, and tokens are compared exactly, so a non-Unicode column is enough.
        builder.Property(t => t.Token).HasMaxLength(FieldLengths.PreviewToken).IsUnicode(false);

        builder.HasIndex(t => t.Token).IsUnique();

        // A trashed post's preview links stop working until it is restored.
        builder.HasQueryFilter(QueryFilters.SoftDelete, t => !t.Post.IsDeleted);
    }
}
