using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VideoChangeCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChangesBaselineAt",
                table: "catalogue_video",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "video_change_state",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LowerBoundAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BaselineCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CaughtUpAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CaughtUpObservedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_change_state", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "video_change_state");

            migrationBuilder.DropColumn(
                name: "ChangesBaselineAt",
                table: "catalogue_video");
        }
    }
}
