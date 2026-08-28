using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheLibraryAndArrivingFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeleteLeftovers",
                table: "installation",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Storage",
                table: "download",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "arriving_file",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IndexerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DerivedReleaseId = table.Column<string>(type: "TEXT", nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: false),
                    ArrivedName = table.Column<string>(type: "TEXT", nullable: false),
                    IsOnDisk = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    RuntimeSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    VideoCodec = table.Column<string>(type: "TEXT", nullable: true),
                    QualityLabel = table.Column<string>(type: "TEXT", nullable: true),
                    OsHash = table.Column<string>(type: "TEXT", nullable: true),
                    IntendedPath = table.Column<string>(type: "TEXT", nullable: true),
                    LastAttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Confidence = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ProbeOutcome = table.Column<string>(type: "TEXT", nullable: false),
                    ProbeError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arriving_file", x => x.Id);
                    table.CheckConstraint("CK_arriving_file_reason", "\"Reason\" IS NULL OR \"Reason\" IN ('IdenticalFile','UnreadableQuality','Unidentified','Duplicate','EntryMissing')");
                    table.CheckConstraint("CK_arriving_file_state", "\"State\" IN ('AwaitingIdentification','AwaitingFiling','Filing','Filed')");
                });

            migrationBuilder.CreateTable(
                name: "confirmed_assignment",
                columns: table => new
                {
                    OsHash = table.Column<string>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserHash = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ArrivalFileName = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseName = table.Column<string>(type: "TEXT", nullable: false),
                    RuntimeSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    VideoCodec = table.Column<string>(type: "TEXT", nullable: true),
                    PrdbAnswer = table.Column<string>(type: "TEXT", nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmed_assignment", x => new { x.OsHash, x.VideoId, x.UserHash });
                });

            migrationBuilder.CreateTable(
                name: "gate_admission",
                columns: table => new
                {
                    Gate = table.Column<string>(type: "TEXT", nullable: false),
                    Confidence = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_admission", x => new { x.Gate, x.Confidence });
                });

            migrationBuilder.CreateTable(
                name: "library_entry",
                columns: table => new
                {
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntryDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    FiledAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_entry", x => x.VideoId);
                });

            migrationBuilder.CreateTable(
                name: "operation_log_entry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Act = table.Column<string>(type: "TEXT", nullable: false),
                    VideoFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LibraryEntryVideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DownloadId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PathBefore = table.Column<string>(type: "TEXT", nullable: true),
                    PathAfter = table.Column<string>(type: "TEXT", nullable: true),
                    DisplacedPath = table.Column<string>(type: "TEXT", nullable: true),
                    LeftoverNamesJson = table.Column<string>(type: "TEXT", nullable: true),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_log_entry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "arriving_file_candidate",
                columns: table => new
                {
                    ArrivingFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arriving_file_candidate", x => new { x.ArrivingFileId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_arriving_file_candidate_arriving_file_ArrivingFileId",
                        column: x => x.ArrivingFileId,
                        principalTable: "arriving_file",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_file",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryEntryVideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FiledPath = table.Column<string>(type: "TEXT", nullable: false),
                    QualityLabel = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    RuntimeSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    VideoCodec = table.Column<string>(type: "TEXT", nullable: true),
                    OsHash = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_file", x => x.Id);
                    table.ForeignKey(
                        name: "FK_video_file_library_entry_LibraryEntryVideoId",
                        column: x => x.LibraryEntryVideoId,
                        principalTable: "library_entry",
                        principalColumn: "VideoId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "gate_admission",
                columns: new[] { "Confidence", "Gate" },
                values: new object[,]
                {
                    { "Exact", "AfterDownload" },
                    { "Strong", "AfterDownload" }
                });

            migrationBuilder.UpdateData(
                table: "installation",
                keyColumn: "Id",
                keyValue: 1,
                column: "DeleteLeftovers",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_arriving_file_DownloadId_SourcePath",
                table: "arriving_file",
                columns: new[] { "DownloadId", "SourcePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_arriving_file_Reason",
                table: "arriving_file",
                column: "Reason",
                filter: "\"Reason\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_arriving_file_State",
                table: "arriving_file",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_arriving_file_VideoId",
                table: "arriving_file",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_arriving_file_candidate_VideoId",
                table: "arriving_file_candidate",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_log_entry_VideoId",
                table: "operation_log_entry",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_video_file_LibraryEntryVideoId",
                table: "video_file",
                column: "LibraryEntryVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_video_file_OsHash",
                table: "video_file",
                column: "OsHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "arriving_file_candidate");

            migrationBuilder.DropTable(
                name: "confirmed_assignment");

            migrationBuilder.DropTable(
                name: "gate_admission");

            migrationBuilder.DropTable(
                name: "operation_log_entry");

            migrationBuilder.DropTable(
                name: "video_file");

            migrationBuilder.DropTable(
                name: "arriving_file");

            migrationBuilder.DropTable(
                name: "library_entry");

            migrationBuilder.DropColumn(
                name: "DeleteLeftovers",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "Storage",
                table: "download");
        }
    }
}
