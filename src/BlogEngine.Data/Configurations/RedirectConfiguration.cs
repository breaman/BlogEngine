using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps <see cref="Redirect"/> (design 6.7).
/// </summary>
public sealed class RedirectConfiguration : IEntityTypeConfiguration<Redirect>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Redirect> builder)
    {
        builder.Property(r => r.FromPath).HasMaxLength(FieldLengths.Url);
        builder.Property(r => r.ToPath).HasMaxLength(FieldLengths.Url);

        // A path can only redirect to one place; also the lookup for unmatched public URLs.
        builder.HasIndex(r => r.FromPath).IsUnique();
    }
}
