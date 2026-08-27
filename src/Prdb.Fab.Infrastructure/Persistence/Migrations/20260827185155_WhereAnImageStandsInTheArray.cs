using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhereAnImageStandsInTheArray : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_catalogue_image_VideoId",
                table: "catalogue_image");

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "catalogue_image",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_image_Cached",
                table: "catalogue_image",
                column: "Cached",
                filter: "\"Cached\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_image_VideoId_Position_PrdbId",
                table: "catalogue_image",
                columns: new[] { "VideoId", "Position", "PrdbId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_catalogue_image_Cached",
                table: "catalogue_image");

            migrationBuilder.DropIndex(
                name: "IX_catalogue_image_VideoId_Position_PrdbId",
                table: "catalogue_image");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "catalogue_image");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_image_VideoId",
                table: "catalogue_image",
                column: "VideoId");
        }
    }
}
