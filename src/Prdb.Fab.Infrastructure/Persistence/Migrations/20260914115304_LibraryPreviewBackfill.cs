using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LibraryPreviewBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BackfillId",
                table: "preview_publication",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "preview_backfill",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserHash = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Selected = table.Column<int>(type: "INTEGER", nullable: false),
                    TakenUp = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preview_backfill", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_preview_publication_BackfillId_State",
                table: "preview_publication",
                columns: new[] { "BackfillId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_preview_backfill_State_RequestedAt",
                table: "preview_backfill",
                columns: new[] { "State", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preview_backfill");

            migrationBuilder.DropIndex(
                name: "IX_preview_publication_BackfillId_State",
                table: "preview_publication");

            migrationBuilder.DropColumn(
                name: "BackfillId",
                table: "preview_publication");
        }
    }
}
