using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlogEngine.Data.Configurations;

/// <summary>
/// Maps the single-row <see cref="SiteSettings"/> table and seeds its only row (design 13).
/// </summary>
public sealed class SiteSettingsConfiguration : IEntityTypeConfiguration<SiteSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SiteSettings> builder)
    {
        // The ID is fixed rather than generated, and the check constraint makes a second row impossible.
        builder.ToTable(table => table.HasCheckConstraint("CK_SiteSettings_Singleton",
            $"[{nameof(SiteSettings.Id)}] = {SiteSettings.SingletonId}"));
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.SiteTitle).HasMaxLength(FieldLengths.SiteTitle);
        builder.Property(s => s.Tagline).HasMaxLength(FieldLengths.Tagline);
        builder.Property(s => s.Description).HasMaxLength(FieldLengths.MetaDescription);
        builder.Property(s => s.AuthorName).HasMaxLength(FieldLengths.PersonName);
        builder.Property(s => s.AuthorBioMarkdown).HasMaxLength(FieldLengths.AuthorBio);
        builder.Property(s => s.TimeZoneId).HasMaxLength(FieldLengths.TimeZoneId);
        builder.Property(s => s.DateFormat).HasMaxLength(FieldLengths.DateFormat);
        builder.Property(s => s.RobotsTxtExtras).HasMaxLength(FieldLengths.RobotsTxtExtras);

        // An open-ended list of networks, so it is stored as JSON instead of one column per network.
        builder.ComplexCollection(s => s.SocialLinks, links => links.ToJson());

        // Media references are optional and never cascade: deleting the media item is blocked while it
        // is in use, and SQL Server would reject three cascade paths from MediaItem to this table anyway.
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(s => s.AuthorAvatarMediaId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(s => s.FaviconMediaId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(s => s.DefaultSocialImageMediaId)
            .OnDelete(DeleteBehavior.Restrict);

        // The row itself is seeded by an explicit InsertData in the InitialBlogSchema migration, not HasData:
        // HasData skips complex collections, so the seeded row would omit the NOT NULL SocialLinks column.
    }
}
