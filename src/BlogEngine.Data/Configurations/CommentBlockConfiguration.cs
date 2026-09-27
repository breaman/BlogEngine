using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="CommentBlock"/> (design 6.5).
/// </summary>
public sealed class CommentBlockConfiguration : IEntityTypeConfiguration<CommentBlock>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CommentBlock> builder)
    {
        builder.Property(b => b.Value).HasMaxLength(FieldLengths.CommentBlockValue);
        builder.Property(b => b.Note).HasMaxLength(FieldLengths.CommentBlockNote);

        // Blocking the same value twice is meaningless; the index also serves spam guard lookups.
        builder.HasIndex(b => new { b.Kind, b.Value }).IsUnique();
    }
}