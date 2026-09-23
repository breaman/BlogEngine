using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="MediaItem"/> and its renditions (design 6.6).
/// </summary>
public sealed class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.Property(m => m.PublicId).HasMaxLength(FieldLengths.MediaPublicId).IsFixedLength().IsUnicode(false);
        builder.Property(m => m.FileName).HasMaxLength(FieldLengths.MediaFileName);
        builder.Property(m => m.OriginalStorageKey).HasMaxLength(FieldLengths.StorageKey);
        builder.Property(m => m.CurrentStorageKey).HasMaxLength(FieldLengths.StorageKey);
        builder.Property(m => m.ContentType).HasMaxLength(FieldLengths.ContentType);
        builder.Property(m => m.AltText).HasMaxLength(FieldLengths.AltText);
        builder.Property(m => m.Caption).HasMaxLength(FieldLengths.Caption);
        builder.Property(m => m.ContentHash).HasMaxLength(FieldLengths.Sha256Hex).IsFixedLength().IsUnicode(false);

        builder.HasIndex(m => m.PublicId).IsUnique();

        // Not unique: a duplicate upload only produces a warning (design 9.1).
        builder.HasIndex(m => m.ContentHash);

        builder.HasMany(m => m.Renditions)
            .WithOne(r => r.MediaItem)
            .HasForeignKey(r => r.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.PostMedia)
            .WithOne(pm => pm.MediaItem)
            .HasForeignKey(pm => pm.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
