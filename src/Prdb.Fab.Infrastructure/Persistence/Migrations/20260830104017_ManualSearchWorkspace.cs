using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualSearchWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manual_search",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<long>(type: "INTEGER", nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_search", x => x.Id);
                    table.ForeignKey(
                        name: "FK_manual_search_catalogue_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "catalogue_video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manual_search_indexer",
                columns: table => new
                {
                    SearchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IndexerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeferredUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResultsSeen = table.Column<int>(type: "INTEGER", nullable: false),
                    RowsAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_search_indexer", x => new { x.SearchId, x.IndexerId });
                    table.CheckConstraint("CK_manual_search_indexer_state", "\"State\" IN ('Queued','Searching','Deferred','Searched','Failed')");
                    table.ForeignKey(
                        name: "FK_manual_search_indexer_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_manual_search_indexer_manual_search_SearchId",
                        column: x => x.SearchId,
                        principalTable: "manual_search",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manual_search_result",
                columns: table => new
                {
                    SearchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manual_search_result", x => new { x.SearchId, x.ReleaseId });
                    table.ForeignKey(
                        name: "FK_manual_search_result_manual_search_SearchId",
                        column: x => x.SearchId,
                        principalTable: "manual_search",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_manual_search_result_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_manual_search_VideoId_RequestedAt",
                table: "manual_search",
                columns: new[] { "VideoId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_manual_search_indexer_IndexerId",
                table: "manual_search_indexer",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_manual_search_result_ReleaseId",
                table: "manual_search_result",
                column: "ReleaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manual_search_indexer");

            migrationBuilder.DropTable(
                name: "manual_search_result");

            migrationBuilder.DropTable(
                name: "manual_search");
        }
    }
}
