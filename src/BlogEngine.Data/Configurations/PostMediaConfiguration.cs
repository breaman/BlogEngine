using BlogEngine.Data.Common;
using BlogEngine.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps the <see cref="PostMedia"/> usage-tracking join table (design 6.6). Relationships are configured
/// in <see cref="PostConfiguration"/> and <see cref="MediaItemConfiguration"/>.
/// </summary>
public sealed class PostMediaConfiguration : IEntityTypeConfiguration<PostMedia>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PostMedia> builder)
    {
        builder.HasKey(pm => new { pm.PostId, pm.MediaItemId });

        // The primary key covers PostId lookups; "used in N posts" looks up by media item.
        builder.HasIndex(pm => pm.MediaItemId);

        // Matches the post filter. Media deletion checks must use IgnoreQueryFilters so an image used only
        // by a trashed post is still reported as in use (restoring the post would otherwise break it).
        builder.HasQueryFilter(QueryFilters.SoftDelete, pm => !pm.Post.IsDeleted);
    }
}