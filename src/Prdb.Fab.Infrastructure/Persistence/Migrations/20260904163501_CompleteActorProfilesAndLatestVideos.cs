using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteActorProfilesAndLatestVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "Birthday",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BirthdayType",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BirthdayTypeLabel",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Birthplace",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BraSize",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BraSizeLabel",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BreastType",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BreastTypeLabel",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CareerEnd",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CareerStart",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Deathday",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ethnicity",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EthnicityLabel",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Eyecolor",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EyecolorLabel",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenderLabel",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Haircolor",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HaircolorLabel",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HipSize",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Nationality",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalityLabel",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Piercings",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tattoos",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "catalogue_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WaistSize",
                table: "catalogue_actor",
                type: "INTEGER",
                nullable: true);

            // Older installations used this stamp to mean that profile artwork
            // had been checked. The same fallback routine now projects the
            // complete Actor document, so every existing row needs one pass.
            migrationBuilder.Sql("UPDATE catalogue_actor SET ProfileCheckedAt = NULL;");

            migrationBuilder.CreateTable(
                name: "actor_video_load_state",
                columns: table => new
                {
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    ResumePage = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    VideosSeen = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actor_video_load_state", x => x.ActorId);
                    table.ForeignKey(
                        name: "FK_actor_video_load_state_catalogue_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "catalogue_actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_actor_alias",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SitePrdbId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_actor_alias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalogue_actor_alias_catalogue_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "catalogue_actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_actor_bio",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_actor_bio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalogue_actor_bio_catalogue_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "catalogue_actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_actor_image",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageType = table.Column<int>(type: "INTEGER", nullable: true),
                    ImageTypeLabel = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_actor_image", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalogue_actor_image_catalogue_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "catalogue_actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_actor_link",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    ExternalSite = table.Column<int>(type: "INTEGER", nullable: true),
                    ExternalSiteLabel = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_actor_link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalogue_actor_link_catalogue_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "catalogue_actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "actor_video_load_video",
                columns: table => new
                {
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    VideoId = table.Column<long>(type: "INTEGER", nullable: false),
                    LoadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actor_video_load_video", x => new { x.ActorId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_actor_video_load_video_actor_video_load_state_ActorId",
                        column: x => x.ActorId,
                        principalTable: "actor_video_load_state",
                        principalColumn: "ActorId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_actor_video_load_video_catalogue_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "catalogue_video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_actor_video_load_video_VideoId",
                table: "actor_video_load_video",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_actor_alias_ActorId_Name_SitePrdbId",
                table: "catalogue_actor_alias",
                columns: new[] { "ActorId", "Name", "SitePrdbId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_actor_bio_ActorId",
                table: "catalogue_actor_bio",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_actor_bio_PrdbId",
                table: "catalogue_actor_bio",
                column: "PrdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_actor_image_ActorId_Position",
                table: "catalogue_actor_image",
                columns: new[] { "ActorId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_actor_image_PrdbId",
                table: "catalogue_actor_image",
                column: "PrdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_actor_link_ActorId_ExternalSite_Url",
                table: "catalogue_actor_link",
                columns: new[] { "ActorId", "ExternalSite", "Url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actor_video_load_video");

            migrationBuilder.DropTable(
                name: "catalogue_actor_alias");

            migrationBuilder.DropTable(
                name: "catalogue_actor_bio");

            migrationBuilder.DropTable(
                name: "catalogue_actor_image");

            migrationBuilder.DropTable(
                name: "catalogue_actor_link");

            migrationBuilder.DropTable(
                name: "actor_video_load_state");

            migrationBuilder.DropColumn(
                name: "Birthday",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "BirthdayType",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "BirthdayTypeLabel",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Birthplace",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "BraSize",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "BraSizeLabel",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "BreastType",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "BreastTypeLabel",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "CareerEnd",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "CareerStart",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Deathday",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Ethnicity",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "EthnicityLabel",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Eyecolor",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "EyecolorLabel",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "GenderLabel",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Haircolor",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "HaircolorLabel",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "HipSize",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "NationalityLabel",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Piercings",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "Tattoos",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "catalogue_actor");

            migrationBuilder.DropColumn(
                name: "WaistSize",
                table: "catalogue_actor");
        }
    }
}
