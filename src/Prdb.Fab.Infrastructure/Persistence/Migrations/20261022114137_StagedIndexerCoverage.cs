using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StagedIndexerCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "indexer_window",
                columns: table => new
                {
                    IndexerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Days = table.Column<int>(type: "INTEGER", nullable: false),
                    ResumeOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    AnchorReleaseId = table.Column<string>(type: "TEXT", nullable: true),
                    PassStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProofStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OldestPostDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PassQueries = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPassQueries = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_window", x => new { x.IndexerId, x.Days });
                    table.CheckConstraint("CK_indexer_window_days", "Days IN (14,30,90)");
                    table.ForeignKey(
                        name: "FK_indexer_window_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indexer_window");
        }
    }
}
