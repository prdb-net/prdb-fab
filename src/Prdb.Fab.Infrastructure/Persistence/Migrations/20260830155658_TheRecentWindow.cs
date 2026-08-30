using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheRecentWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BootstrapCompletedAt",
                table: "indexer_walk_state");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIdentifiedAt",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecentWindowCompletedAt",
                table: "indexer_walk_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecentWindowOldestPostDate",
                table: "indexer_walk_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecentWindowPassStartedAt",
                table: "indexer_walk_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecentWindowResumePage",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "recent_window_state",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogueResumePage = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    CataloguePassStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CatalogueCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CatalogueOldestCreatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recent_window_state", x => x.Id);
                    table.CheckConstraint("CK_recent_window_state_singleton", "\"Id\" = 1");
                });

            migrationBuilder.InsertData(
                table: "recent_window_state",
                columns: new[] { "Id", "CatalogueCompletedAt", "CatalogueOldestCreatedAt", "CataloguePassStartedAt", "CatalogueResumePage" },
                values: new object[] { 1, null, null, null, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_release_PostDate_LastIdentifiedAt",
                table: "release",
                columns: new[] { "PostDate", "LastIdentifiedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recent_window_state");

            migrationBuilder.DropIndex(
                name: "IX_release_PostDate_LastIdentifiedAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "LastIdentifiedAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "RecentWindowCompletedAt",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "RecentWindowOldestPostDate",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "RecentWindowPassStartedAt",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "RecentWindowResumePage",
                table: "indexer_walk_state");

            migrationBuilder.AddColumn<DateTime>(
                name: "BootstrapCompletedAt",
                table: "indexer_walk_state",
                type: "TEXT",
                nullable: true);
        }
    }
}
