using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncThroughput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetailRetryAt",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificationAnswerJson",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IdentificationDetailsPending",
                table: "release",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_release_IdentificationDetailsPending_DetailRetryAt",
                table: "release",
                columns: new[] { "IdentificationDetailsPending", "DetailRetryAt" });

            migrationBuilder.AddColumn<string>(
                name: "IdentificationInputKey",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_release_IdentificationInputKey_LastIdentifiedAt",
                table: "release",
                columns: new[] { "IdentificationInputKey", "LastIdentifiedAt" });

            migrationBuilder.AddColumn<DateTime>(
                name: "HeadLastSucceededAt",
                table: "indexer_walk_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeadPagesPerPoll",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HeadQueriesSpentToday",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HistoryQueriesSpentToday",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinimumPassQueries",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinimumQueriesSpentToday",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

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

            migrationBuilder.DropColumn(
                name: "HeadLastSucceededAt",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "HeadPagesPerPoll",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "HeadQueriesSpentToday",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "HistoryQueriesSpentToday",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "MinimumPassQueries",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "MinimumQueriesSpentToday",
                table: "indexer_walk_state");

            migrationBuilder.DropIndex(
                name: "IX_release_IdentificationInputKey_LastIdentifiedAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "IdentificationInputKey",
                table: "release");

            migrationBuilder.DropIndex(
                name: "IX_release_IdentificationDetailsPending_DetailRetryAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "DetailRetryAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "IdentificationAnswerJson",
                table: "release");

            migrationBuilder.DropColumn(
                name: "IdentificationDetailsPending",
                table: "release");
        }
    }
}
