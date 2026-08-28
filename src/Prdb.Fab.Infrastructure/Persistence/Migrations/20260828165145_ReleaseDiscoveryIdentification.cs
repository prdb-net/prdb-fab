using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseDiscoveryIdentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SweepQueriesSpentToday",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_release_SiteId",
                table: "release",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_video_pre_name_SearchedBackwards",
                table: "catalogue_video_pre_name",
                column: "SearchedBackwards");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_video_TitleSearchedBackwards",
                table: "catalogue_video",
                column: "TitleSearchedBackwards");

            migrationBuilder.AddForeignKey(
                name: "FK_release_catalogue_site_SiteId",
                table: "release",
                column: "SiteId",
                principalTable: "catalogue_site",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_release_catalogue_video_VideoId",
                table: "release",
                column: "VideoId",
                principalTable: "catalogue_video",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_release_catalogue_site_SiteId",
                table: "release");

            migrationBuilder.DropForeignKey(
                name: "FK_release_catalogue_video_VideoId",
                table: "release");

            migrationBuilder.DropIndex(
                name: "IX_release_SiteId",
                table: "release");

            migrationBuilder.DropIndex(
                name: "IX_catalogue_video_pre_name_SearchedBackwards",
                table: "catalogue_video_pre_name");

            migrationBuilder.DropIndex(
                name: "IX_catalogue_video_TitleSearchedBackwards",
                table: "catalogue_video");

            migrationBuilder.DropColumn(
                name: "SweepQueriesSpentToday",
                table: "indexer_walk_state");
        }
    }
}
