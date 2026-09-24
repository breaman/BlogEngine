using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostSocialImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SocialImageMediaId",
                table: "Posts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_SocialImageMediaId",
                table: "Posts",
                column: "SocialImageMediaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_MediaItems_SocialImageMediaId",
                table: "Posts",
                column: "SocialImageMediaId",
                principalTable: "MediaItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_MediaItems_SocialImageMediaId",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Posts_SocialImageMediaId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "SocialImageMediaId",
                table: "Posts");
        }
    }
}
