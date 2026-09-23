using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialBlogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<string>(type: "char(12)", unicode: false, fixedLength: true, maxLength: 12, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalStorageKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CurrentStorageKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EditOperationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ShowInNav = table.Column<bool>(type: "bit", nullable: false),
                    NavOrder = table.Column<int>(type: "int", nullable: false),
                    MetaTitle = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Redirects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ToPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    HitCount = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Redirects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaRenditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MediaItemId = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRenditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaRenditions_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PublishedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    PublishedDateLocal = table.Column<DateOnly>(type: "date", nullable: true),
                    LastUpdatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CoverMediaId = table.Column<int>(type: "int", nullable: true),
                    AllowComments = table.Column<bool>(type: "bit", nullable: false),
                    CommentsCloseOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false),
                    WordCount = table.Column<int>(type: "int", nullable: false),
                    ReadingMinutes = table.Column<int>(type: "int", nullable: false),
                    MetaTitle = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Posts_MediaItems_CoverMediaId",
                        column: x => x.CoverMediaId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    SiteTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tagline = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    AuthorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AuthorBioMarkdown = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AuthorAvatarMediaId = table.Column<int>(type: "int", nullable: true),
                    FaviconMediaId = table.Column<int>(type: "int", nullable: true),
                    PostsPerPage = table.Column<int>(type: "int", nullable: false),
                    FeedContentMode = table.Column<int>(type: "int", nullable: false),
                    HomePageMode = table.Column<int>(type: "int", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DateFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CommentsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RequireCommentApproval = table.Column<bool>(type: "bit", nullable: false),
                    AutoApproveReturningCommenters = table.Column<bool>(type: "bit", nullable: false),
                    CloseCommentsAfterDays = table.Column<int>(type: "int", nullable: false),
                    MaxCommentLinks = table.Column<int>(type: "int", nullable: false),
                    ShowCommentAvatars = table.Column<bool>(type: "bit", nullable: false),
                    NotifyOnPendingComment = table.Column<bool>(type: "bit", nullable: false),
                    MaxUploadSizeMegabytes = table.Column<int>(type: "int", nullable: false),
                    DownscaleOriginalsAbovePixels = table.Column<int>(type: "int", nullable: false),
                    RenditionWidths = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultSocialImageMediaId = table.Column<int>(type: "int", nullable: true),
                    RobotsTxtExtras = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DiscourageSearchEngines = table.Column<bool>(type: "bit", nullable: false),
                    AllowRegistration = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false),
                    SocialLinks = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                    table.CheckConstraint("CK_SiteSettings_Singleton", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_SiteSettings_MediaItems_AuthorAvatarMediaId",
                        column: x => x.AuthorAvatarMediaId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteSettings_MediaItems_DefaultSocialImageMediaId",
                        column: x => x.DefaultSocialImageMediaId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SiteSettings_MediaItems_FaviconMediaId",
                        column: x => x.FaviconMediaId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    ParentCommentId = table.Column<int>(type: "int", nullable: true),
                    AuthorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthorEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    AuthorUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BodyMarkdown = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsAuthorReply = table.Column<bool>(type: "bit", nullable: false),
                    IpHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SpamScore = table.Column<int>(type: "int", nullable: false),
                    SpamReasons = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ModeratedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModeratedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostMedia",
                columns: table => new
                {
                    PostId = table.Column<int>(type: "int", nullable: false),
                    MediaItemId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostMedia", x => new { x.PostId, x.MediaItemId });
                    table.ForeignKey(
                        name: "FK_PostMedia_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostMedia_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SavedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostRevisions_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostTags",
                columns: table => new
                {
                    PostId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostTags", x => new { x.PostId, x.TagId });
                    table.ForeignKey(
                        name: "FK_PostTags_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreviewTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExpiresOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    CreatedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ModifiedOn = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ModifiedBy = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreviewTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreviewTokens_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommentBlocks_Kind_Value",
                table: "CommentBlocks",
                columns: new[] { "Kind", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_AuthorEmail",
                table: "Comments",
                column: "AuthorEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_IpHash",
                table: "Comments",
                column: "IpHash");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ParentCommentId",
                table: "Comments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PostId_Status",
                table: "Comments",
                columns: new[] { "PostId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_Status",
                table: "Comments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ContentHash",
                table: "MediaItems",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_PublicId",
                table: "MediaItems",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaRenditions_MediaItemId_Width_Format",
                table: "MediaRenditions",
                columns: new[] { "MediaItemId", "Width", "Format" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pages_Slug",
                table: "Pages",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostMedia_MediaItemId",
                table: "PostMedia",
                column: "MediaItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PostRevisions_PostId_Kind_SavedOn",
                table: "PostRevisions",
                columns: new[] { "PostId", "Kind", "SavedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CoverMediaId",
                table: "Posts",
                column: "CoverMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_PublishedDateLocal_Slug",
                table: "Posts",
                columns: new[] { "PublishedDateLocal", "Slug" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Slug",
                table: "Posts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Status_PublishedOn",
                table: "Posts",
                columns: new[] { "Status", "PublishedOn" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PostTags_TagId",
                table: "PostTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_PreviewTokens_PostId",
                table: "PreviewTokens",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_PreviewTokens_Token",
                table: "PreviewTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_FromPath",
                table: "Redirects",
                column: "FromPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_AuthorAvatarMediaId",
                table: "SiteSettings",
                column: "AuthorAvatarMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_DefaultSocialImageMediaId",
                table: "SiteSettings",
                column: "DefaultSocialImageMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteSettings_FaviconMediaId",
                table: "SiteSettings",
                column: "FaviconMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_NormalizedName",
                table: "Tags",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Slug",
                table: "Tags",
                column: "Slug",
                unique: true);

            // Seed the single settings row (design 13). Raw SQL rather than HasData or InsertData: HasData skips
            // complex collections (leaving the NOT NULL SocialLinks column out), and InsertData can't resolve
            // a JSON column that has no scalar property. Values are literals, not SiteSettingsDefaults, so
            // later changes to the defaults can't rewrite what this migration did.
            migrationBuilder.Sql("""
                INSERT INTO [SiteSettings] (
                    [Id], [SiteTitle], [SocialLinks], [PostsPerPage], [FeedContentMode], [HomePageMode],
                    [TimeZoneId], [DateFormat], [CommentsEnabled], [RequireCommentApproval],
                    [AutoApproveReturningCommenters], [CloseCommentsAfterDays], [MaxCommentLinks],
                    [ShowCommentAvatars], [NotifyOnPendingComment], [MaxUploadSizeMegabytes],
                    [DownscaleOriginalsAbovePixels], [RenditionWidths], [DiscourageSearchEngines],
                    [AllowRegistration], [CreatedBy], [ModifiedBy])
                VALUES (
                    1, N'Breaman Blog', N'[]', 10, 0, 0,
                    N'UTC', N'MMMM d, yyyy', 1, 1,
                    0, 0, 2,
                    0, 0, 20,
                    0, N'[320,640,960,1280,1920]', 0,
                    0, 0, 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentBlocks");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "MediaRenditions");

            migrationBuilder.DropTable(
                name: "Pages");

            migrationBuilder.DropTable(
                name: "PostMedia");

            migrationBuilder.DropTable(
                name: "PostRevisions");

            migrationBuilder.DropTable(
                name: "PostTags");

            migrationBuilder.DropTable(
                name: "PreviewTokens");

            migrationBuilder.DropTable(
                name: "Redirects");

            migrationBuilder.DropTable(
                name: "SiteSettings");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropTable(
                name: "MediaItems");
        }
    }
}
