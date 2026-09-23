using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="Comment"/> and its one-level reply thread (design 6.5).
/// </summary>
public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.Property(c => c.AuthorName).HasMaxLength(FieldLengths.PersonName);
        builder.Property(c => c.AuthorEmail).HasMaxLength(FieldLengths.Email);
        builder.Property(c => c.AuthorUrl).HasMaxLength(FieldLengths.Url);
        builder.Property(c => c.BodyMarkdown).HasMaxLength(FieldLengths.CommentBody);
        builder.Property(c => c.IpHash).HasMaxLength(FieldLengths.Sha256Hex).IsFixedLength().IsUnicode(false);
        builder.Property(c => c.UserAgent).HasMaxLength(FieldLengths.UserAgent);
        builder.Property(c => c.SpamReasons).HasMaxLength(FieldLengths.SpamReasons);

        // SQL Server does not allow cascading self-references. Deleting a post still removes all of its
        // comments in one statement; deleting a single comment must deal with its replies explicitly.
        builder.HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Post page (approved comments of a post), moderation queue tabs, and block/auto-approve lookups.
        builder.HasIndex(c => new { c.PostId, c.Status });
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.AuthorEmail);
        builder.HasIndex(c => c.IpHash);

        // Comments on trashed posts disappear from the moderation queue until the post is restored.
        builder.HasQueryFilter(QueryFilters.SoftDelete, c => !c.Post.IsDeleted);
    }
}
