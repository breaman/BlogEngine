using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="Tag"/> (design 6.4).
/// </summary>
public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(FieldLengths.TagName);
        builder.Property(t => t.NormalizedName).HasMaxLength(FieldLengths.TagName);
        builder.Property(t => t.Slug).HasMaxLength(FieldLengths.TagSlug);
        builder.Property(t => t.Description).HasMaxLength(FieldLengths.TagDescription);

        // The final guard against duplicate tags when two saves race: the loser hits this index,
        // reloads, and reuses the winner's tag.
        builder.HasIndex(t => t.NormalizedName).IsUnique();
        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
