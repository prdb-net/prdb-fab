using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhenReleasesAreDiscovered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResultsSeen",
                table: "routine_run",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RowsAdded",
                table: "routine_run",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DailyQueryBudget",
                table: "indexer",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "indexer",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "indexer",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "identification_outcome",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Gate = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identification_outcome", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "indexer_walk_state",
                columns: table => new
                {
                    IndexerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WatermarkPostDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WatermarkReleaseId = table.Column<string>(type: "TEXT", nullable: true),
                    CapsTree = table.Column<string>(type: "TEXT", nullable: false),
                    ResolvedCategoryIds = table.Column<string>(type: "TEXT", nullable: false),
                    MissingCategoryNames = table.Column<string>(type: "TEXT", nullable: false),
                    CapsCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QueryDay = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QueriesSpentToday = table.Column<int>(type: "INTEGER", nullable: false),
                    ResumePage = table.Column<int>(type: "INTEGER", nullable: true),
                    BootstrapCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CatchUpFrom = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CatchUpTo = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CatchUpCause = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_walk_state", x => x.IndexerId);
                    table.ForeignKey(
                        name: "FK_indexer_walk_state_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "release",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DerivedReleaseId = table.Column<string>(type: "TEXT", nullable: false),
                    RawGuid = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    NormalisedTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: true),
                    Categories = table.Column<string>(type: "TEXT", nullable: false),
                    PostDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PubDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IdentificationState = table.Column<string>(type: "TEXT", nullable: false),
                    VideoId = table.Column<long>(type: "INTEGER", nullable: true),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: true),
                    MatchedBy = table.Column<string>(type: "TEXT", nullable: true),
                    SiteId = table.Column<long>(type: "INTEGER", nullable: true),
                    SearchWasReason = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release", x => x.Id);
                    table.CheckConstraint("CK_release_identification_state", "\"IdentificationState\" IN ('Unexamined','Unremarkable','Awaiting','Matched','SiteOnly','Ambiguous','Unknown')");
                    table.ForeignKey(
                        name: "FK_release_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wanted_video_sweep_state",
                columns: table => new
                {
                    VideoId = table.Column<long>(type: "INTEGER", nullable: false),
                    IndexerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastSearchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wanted_video_sweep_state", x => new { x.VideoId, x.IndexerId });
                    table.ForeignKey(
                        name: "FK_wanted_video_sweep_state_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wanted_video_sweep_state_wanted_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "wanted_video",
                        principalColumn: "VideoId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "release_candidate",
                columns: table => new
                {
                    ReleaseId = table.Column<long>(type: "INTEGER", nullable: false),
                    VideoId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_candidate", x => new { x.ReleaseId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_release_candidate_catalogue_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "catalogue_video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_release_candidate_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identification_outcome_At",
                table: "identification_outcome",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_release_FirstSeenAt",
                table: "release",
                column: "FirstSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_release_IdentificationState",
                table: "release",
                column: "IdentificationState");

            migrationBuilder.CreateIndex(
                name: "IX_release_IndexerId_DerivedReleaseId",
                table: "release",
                columns: new[] { "IndexerId", "DerivedReleaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_release_VideoId",
                table: "release",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_release_candidate_ReleaseId",
                table: "release_candidate",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_release_candidate_VideoId",
                table: "release_candidate",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_wanted_video_sweep_state_IndexerId",
                table: "wanted_video_sweep_state",
                column: "IndexerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identification_outcome");

            migrationBuilder.DropTable(
                name: "indexer_walk_state");

            migrationBuilder.DropTable(
                name: "release_candidate");

            migrationBuilder.DropTable(
                name: "wanted_video_sweep_state");

            migrationBuilder.DropTable(
                name: "release");

            migrationBuilder.DropColumn(
                name: "ResultsSeen",
                table: "routine_run");

            migrationBuilder.DropColumn(
                name: "RowsAdded",
                table: "routine_run");

            migrationBuilder.DropColumn(
                name: "DailyQueryBudget",
                table: "indexer");

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "indexer");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "indexer");
        }
    }
}
