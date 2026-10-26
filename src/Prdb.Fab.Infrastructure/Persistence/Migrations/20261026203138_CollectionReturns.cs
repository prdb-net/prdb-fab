using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CollectionReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_undo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollectionFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: false),
                    DestinationPath = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRoot = table.Column<string>(type: "TEXT", nullable: false),
                    DestinationRoot = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    OsHash = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: true),
                    Approved = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_undo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collection_undo_CollectionFileId",
                table: "collection_undo",
                column: "CollectionFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_undo_CompletedAt_Approved_AttemptedAt",
                table: "collection_undo",
                columns: new[] { "CompletedAt", "Approved", "AttemptedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_undo");
        }
    }
}
