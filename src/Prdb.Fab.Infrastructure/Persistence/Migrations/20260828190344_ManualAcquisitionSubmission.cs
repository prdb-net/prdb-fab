using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualAcquisitionSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Password",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryBudget",
                table: "installation",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "download",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IndexerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DerivedReleaseId = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedName = table.Column<string>(type: "TEXT", nullable: false),
                    NzoId = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Cause = table.Column<string>(type: "TEXT", nullable: true),
                    LastSabnzbdStatus = table.Column<string>(type: "TEXT", nullable: true),
                    FailMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StageLog = table.Column<string>(type: "TEXT", nullable: true),
                    ConsecutiveAbsences = table.Column<int>(type: "INTEGER", nullable: false),
                    OutstandingSince = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TidiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginIsPerson = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "release_not_downloaded",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_not_downloaded", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "installation",
                keyColumn: "Id",
                keyValue: 1,
                column: "RetryBudget",
                value: 3);

            migrationBuilder.CreateIndex(
                name: "IX_download_State_CreatedAt",
                table: "download",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_download_VideoId",
                table: "download",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_download_VideoId_IndexerId_DerivedReleaseId",
                table: "download",
                columns: new[] { "VideoId", "IndexerId", "DerivedReleaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_release_not_downloaded_At",
                table: "release_not_downloaded",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "download");

            migrationBuilder.DropTable(
                name: "release_not_downloaded");

            migrationBuilder.DropColumn(
                name: "Password",
                table: "release");

            migrationBuilder.DropColumn(
                name: "RetryBudget",
                table: "installation");
        }
    }
}
