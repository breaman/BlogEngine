using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="Page"/> (design 6.7, 6.8).
/// </summary>
public sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.Property(p => p.Title).HasMaxLength(FieldLengths.PostTitle);
        builder.Property(p => p.Slug).HasMaxLength(FieldLengths.Slug);
        builder.Property(p => p.Summary).HasMaxLength(FieldLengths.PostSummary);
        builder.Property(p => p.MetaTitle).HasMaxLength(FieldLengths.MetaTitle);
        builder.Property(p => p.MetaDescription).HasMaxLength(FieldLengths.MetaDescription);

        builder.HasIndex(p => p.Slug).IsUnique();

        builder.HasQueryFilter(QueryFilters.SoftDelete, p => !p.IsDeleted);
    }
}
