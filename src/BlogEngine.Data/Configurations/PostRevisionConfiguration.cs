using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="PostRevision"/> (design 6.7).
/// </summary>
public sealed class PostRevisionConfiguration : IEntityTypeConfiguration<PostRevision>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PostRevision> builder)
    {
        builder.Property(r => r.Title).HasMaxLength(FieldLengths.PostTitle);

        // Revision history lists and autosave pruning both read one post's revisions of a kind by date.
        builder.HasIndex(r => new { r.PostId, r.Kind, r.SavedOn });

        builder.HasQueryFilter(QueryFilters.SoftDelete, r => !r.Post.IsDeleted);
    }
}