using BlogEngine.Data.Common;
using BlogEngine.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps the <see cref="PostTag"/> join table. Its relationships are configured with the many-to-many
/// in <see cref="PostConfiguration"/>.
/// </summary>
public sealed class PostTagConfiguration : IEntityTypeConfiguration<PostTag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PostTag> builder)
    {
        builder.HasKey(pt => new { pt.PostId, pt.TagId });

        // Matches the post filter so tag counts and tag pages never include trashed posts. It also keeps
        // EF from warning that a required navigation points at a filtered entity.
        builder.HasQueryFilter(QueryFilters.SoftDelete, pt => !pt.Post.IsDeleted);
    }
}