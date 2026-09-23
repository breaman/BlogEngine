using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostHasCodeBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasCodeBlocks",
                table: "Posts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Flag existing posts from their stored HTML: every code block, fenced or indented, renders as <pre>.
            // Posts saved from now on get the flag from the Markdown render itself.
            migrationBuilder.Sql("UPDATE [Posts] SET [HasCodeBlocks] = 1 WHERE [ContentHtml] LIKE N'%<pre%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasCodeBlocks",
                table: "Posts");
        }
    }
}
