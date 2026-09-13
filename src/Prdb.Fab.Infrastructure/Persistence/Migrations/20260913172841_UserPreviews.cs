using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_preview",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoPrdbId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OsHash = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    VttUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Filesize = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    TileCount = table.Column<int>(type: "INTEGER", nullable: true),
                    TileWidth = table.Column<int>(type: "INTEGER", nullable: true),
                    TileHeight = table.Column<int>(type: "INTEGER", nullable: true),
                    Columns = table.Column<int>(type: "INTEGER", nullable: true),
                    Rows = table.Column<int>(type: "INTEGER", nullable: true),
                    ModerationStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ModerationVisibility = table.Column<string>(type: "TEXT", nullable: true),
                    ShownUnder = table.Column<string>(type: "TEXT", nullable: true),
                    Shown = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CachedVersion = table.Column<string>(type: "TEXT", nullable: true),
                    LastServedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_preview", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_preview_interest",
                columns: table => new
                {
                    VideoPrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TouchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ForTheLibrary = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_preview_interest", x => x.VideoPrdbId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_preview_LastServedAt",
                table: "user_preview",
                column: "LastServedAt");

            migrationBuilder.CreateIndex(
                name: "IX_user_preview_OsHash",
                table: "user_preview",
                column: "OsHash");

            migrationBuilder.CreateIndex(
                name: "IX_user_preview_PrdbId",
                table: "user_preview",
                column: "PrdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_preview_VideoPrdbId_OsHash_DisplayOrder_PrdbId",
                table: "user_preview",
                columns: new[] { "VideoPrdbId", "OsHash", "DisplayOrder", "PrdbId" });

            migrationBuilder.CreateIndex(
                name: "IX_user_preview_interest_LastReadAt",
                table: "user_preview_interest",
                column: "LastReadAt");

            migrationBuilder.CreateIndex(
                name: "IX_user_preview_interest_TouchedAt",
                table: "user_preview_interest",
                column: "TouchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_preview");

            migrationBuilder.DropTable(
                name: "user_preview_interest");
        }
    }
}
