using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Data.Models;

/// <summary>
/// The application's EF Core context: Identity and audit tables from <see cref="AuthDbContext"/> plus the
/// blog domain (design 6).
/// </summary>
/// <remarks>
/// Each entity is mapped by its own <c>IEntityTypeConfiguration&lt;T&gt;</c> in <c>BlogEngine.Data/Configurations</c>,
/// picked up automatically by <see cref="OnModelCreating"/>.
/// </remarks>
public class ApplicationDbContext : AuthDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostRevision> PostRevisions => Set<PostRevision>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PostTag> PostTags => Set<PostTag>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentBlock> CommentBlocks => Set<CommentBlock>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<MediaRendition> MediaRenditions => Set<MediaRendition>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Redirect> Redirects => Set<Redirect>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<PreviewToken> PreviewTokens => Set<PreviewToken>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity's mappings first, so the blog configurations can build on them.
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
