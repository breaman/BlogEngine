using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps the profile columns <see cref="User"/> adds to the Identity user table.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(FieldLengths.PersonName);
        builder.Property(u => u.LastName).HasMaxLength(FieldLengths.PersonName);
        builder.Property(u => u.DisplayName).HasMaxLength(FieldLengths.PersonName);
    }
}
