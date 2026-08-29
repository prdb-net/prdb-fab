using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InteractiveCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsNewObservedAt",
                table: "installation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WhatsNewObservedVideoId",
                table: "installation",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionState",
                table: "download",
                type: "TEXT",
                nullable: false,
                defaultValue: "Submitted");

            migrationBuilder.AddColumn<Guid>(
                name: "ArtworkCacheKey",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ArtworkCached",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ArtworkFoundDead",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArtworkLastServedAt",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileCheckedAt",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileImageUrl",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_preference_write",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Desired = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastFailure = table.Column<string>(type: "TEXT", nullable: true),
                    Blocked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_preference_write", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "installation",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "WhatsNewObservedAt", "WhatsNewObservedVideoId" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_download_SubmissionState_CreatedAt",
                table: "download",
                columns: new[] { "SubmissionState", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_actor_ArtworkCacheKey",
                table: "catalogue_actor",
                column: "ArtworkCacheKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_preference_write_Blocked_RequestedAt",
                table: "account_preference_write",
                columns: new[] { "Blocked", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_account_preference_write_Kind_EntityId",
                table: "account_preference_write",
                columns: new[] { "Kind", "EntityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_preference_write");

            migrationBuilder.DropIndex(
                name: "IX_download_SubmissionState_CreatedAt",
                table: "download");

            migrationBuilder.DropIndex(
                name: "IX_catalogue_actor_ArtworkCacheKey",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "WhatsNewObservedAt",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "WhatsNewObservedVideoId",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "SubmissionState",
                table: "download");

            migrationBuilder.DropColumn(
                name: "ArtworkCacheKey",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "ArtworkCached",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "ArtworkFoundDead",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "ArtworkLastServedAt",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "ProfileCheckedAt",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "ProfileImageUrl",
                table: "catalogue_actor");
        }
    }
}
