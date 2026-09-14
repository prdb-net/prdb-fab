using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreviewPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preview_publication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoPrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OsHash = table.Column<string>(type: "TEXT", nullable: false),
                    UserHash = table.Column<string>(type: "TEXT", nullable: false),
                    OutputVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Tiles = table.Column<int>(type: "INTEGER", nullable: true),
                    Columns = table.Column<int>(type: "INTEGER", nullable: true),
                    Rows = table.Column<int>(type: "INTEGER", nullable: true),
                    SheetBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IntendedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preview_publication", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_preview_publication_State_IntendedAt",
                table: "preview_publication",
                columns: new[] { "State", "IntendedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_preview_publication_UserHash_OsHash_OutputVersion",
                table: "preview_publication",
                columns: new[] { "UserHash", "OsHash", "OutputVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_preview_publication_VideoFileId",
                table: "preview_publication",
                column: "VideoFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preview_publication");
        }
    }
}
