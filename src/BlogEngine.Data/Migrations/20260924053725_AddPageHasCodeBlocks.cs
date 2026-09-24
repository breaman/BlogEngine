using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageHasCodeBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasCodeBlocks",
                table: "Pages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Flag any existing pages from their stored HTML, as AddPostHasCodeBlocks did for posts.
            migrationBuilder.Sql("UPDATE [Pages] SET [HasCodeBlocks] = 1 WHERE [ContentHtml] LIKE N'%<pre%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasCodeBlocks",
                table: "Pages");
        }
    }
}
