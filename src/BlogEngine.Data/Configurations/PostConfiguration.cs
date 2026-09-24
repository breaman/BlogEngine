using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="Post"/>: slug uniqueness, listing index, concurrency token, soft-delete filter and the
/// relationships owned by a post (design 6.2, 6.8).
/// </summary>
public sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.Property(p => p.Title).HasMaxLength(FieldLengths.PostTitle);
        builder.Property(p => p.Slug).HasMaxLength(FieldLengths.Slug);
        builder.Property(p => p.Summary).HasMaxLength(FieldLengths.PostSummary);
        builder.Property(p => p.MetaTitle).HasMaxLength(FieldLengths.MetaTitle);
        builder.Property(p => p.MetaDescription).HasMaxLength(FieldLengths.MetaDescription);
        builder.Property(p => p.RowVersion).IsRowVersion();

        // Slugs are globally unique (simpler for search and redirects), which includes trashed posts so a
        // restored post can never collide with one created while it was in the trash.
        builder.HasIndex(p => p.Slug).IsUnique();

        // Secondary safeguard for the dated URL /posts/{yyyy}/{mm}/{dd}/{slug}.
        builder.HasIndex(p => new { p.PublishedDateLocal, p.Slug })
            .IsUnique()
            .HasFilter($"[{nameof(Post.Status)}] = {(int)PostStatus.Published}");

        // Newest-first public listings.
        builder.HasIndex(p => new { p.Status, p.PublishedOn }).IsDescending(false, true);

        builder.HasQueryFilter(QueryFilters.SoftDelete, p => !p.IsDeleted);

        // Restrict rather than SetNull: SQL Server rejects a second cascade path from MediaItem to
        // PostMedia (MediaItem -> Post -> PostMedia). Media deletion clears covers explicitly instead.
        builder.HasOne(p => p.CoverMedia)
            .WithMany()
            .HasForeignKey(p => p.CoverMediaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning as the cover: media deletion clears the reference explicitly.
        builder.HasOne(p => p.SocialImageMedia)
            .WithMany()
            .HasForeignKey(p => p.SocialImageMediaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Tags)
            .WithMany(t => t.Posts)
            .UsingEntity<PostTag>(
                right => right.HasOne(pt => pt.Tag).WithMany(t => t.PostTags).HasForeignKey(pt => pt.TagId)
                    .OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne(pt => pt.Post).WithMany(p => p.PostTags).HasForeignKey(pt => pt.PostId)
                    .OnDelete(DeleteBehavior.Cascade));

        // "Empty trash" hard-deletes a post and everything that hangs off it (design 6.8).
        builder.HasMany(p => p.Comments)
            .WithOne(c => c.Post)
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Revisions)
            .WithOne(r => r.Post)
            .HasForeignKey(r => r.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.PostMedia)
            .WithOne(pm => pm.Post)
            .HasForeignKey(pm => pm.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.PreviewTokens)
            .WithOne(t => t.Post)
            .HasForeignKey(t => t.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
