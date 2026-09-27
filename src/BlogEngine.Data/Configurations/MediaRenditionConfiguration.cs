using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="MediaRendition"/> (design 6.6, 9.4).
/// </summary>
public sealed class MediaRenditionConfiguration : IEntityTypeConfiguration<MediaRendition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaRendition> builder)
    {
        builder.Property(r => r.Format).HasMaxLength(FieldLengths.RenditionFormat);
        builder.Property(r => r.StorageKey).HasMaxLength(FieldLengths.StorageKey);

        // One rendition per width and format; also the lookup used by ?w=&f= requests.
        builder.HasIndex(r => new { r.MediaItemId, r.Width, r.Format }).IsUnique();
    }
}