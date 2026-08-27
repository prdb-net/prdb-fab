using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheCatalogueTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalogue_actor",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_actor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_site",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Network = table.Column<string>(type: "TEXT", nullable: true),
                    StillOffered = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_site", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "feed_cursor",
                columns: table => new
                {
                    Feed = table.Column<string>(type: "TEXT", nullable: false),
                    Cursor = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feed_cursor", x => x.Feed);
                });

            migrationBuilder.CreateTable(
                name: "favourite_actor",
                columns: table => new
                {
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false),
                    SinceAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_favourite_actor", x => x.ActorId);
                    table.ForeignKey(
                        name: "FK_favourite_actor_catalogue_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "catalogue_actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_video",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    NormalisedTitle = table.Column<string>(type: "TEXT", nullable: false),
                    SiteId = table.Column<long>(type: "INTEGER", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    DurationSpreadMs = table.Column<long>(type: "INTEGER", nullable: true),
                    DurationFileCount = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TitleSearchedBackwards = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_video", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalogue_video_catalogue_site_SiteId",
                        column: x => x.SiteId,
                        principalTable: "catalogue_site",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "favourite_site",
                columns: table => new
                {
                    SiteId = table.Column<long>(type: "INTEGER", nullable: false),
                    SinceAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_favourite_site", x => x.SiteId);
                    table.ForeignKey(
                        name: "FK_favourite_site_catalogue_site_SiteId",
                        column: x => x.SiteId,
                        principalTable: "catalogue_site",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_image",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrdbId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<long>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Cached = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    FoundDead = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    LastServedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_image", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalogue_image_catalogue_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "catalogue_video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_video_actor",
                columns: table => new
                {
                    VideoId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_video_actor", x => new { x.VideoId, x.ActorId });
                    table.ForeignKey(
                        name: "FK_catalogue_video_actor_catalogue_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "catalogue_actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_catalogue_video_actor_catalogue_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "catalogue_video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "catalogue_video_pre_name",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoId = table.Column<long>(type: "INTEGER", nullable: false),
                    PreName = table.Column<string>(type: "TEXT", nullable: false),
                    NormalisedPreName = table.Column<string>(type: "TEXT", nullable: false),
                    SearchedBackwards = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalogue_video_pre_name", x => x.Id);
                    table.ForeignKey(
                        name: "FK_catalogue_video_pre_name_catalogue_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "catalogue_video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wanted_video",
                columns: table => new
                {
                    VideoId = table.Column<long>(type: "INTEGER", nullable: false),
                    SinceAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wanted_video", x => x.VideoId);
                    table.ForeignKey(
                        name: "FK_wanted_video_catalogue_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "catalogue_video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_actor_PrdbId",
                table: "catalogue_actor",
                column: "PrdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_image_LastServedAt",
                table: "catalogue_image",
                column: "LastServedAt");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_image_PrdbId",
                table: "catalogue_image",
                column: "PrdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_image_VideoId",
                table: "catalogue_image",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_site_PrdbId",
                table: "catalogue_site",
                column: "PrdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_video_PrdbId",
                table: "catalogue_video",
                column: "PrdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_video_SiteId",
                table: "catalogue_video",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_video_actor_ActorId",
                table: "catalogue_video_actor",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_video_pre_name_VideoId_PreName",
                table: "catalogue_video_pre_name",
                columns: new[] { "VideoId", "PreName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalogue_image");

            migrationBuilder.DropTable(
                name: "catalogue_video_actor");

            migrationBuilder.DropTable(
                name: "catalogue_video_pre_name");

            migrationBuilder.DropTable(
                name: "favourite_actor");

            migrationBuilder.DropTable(
                name: "favourite_site");

            migrationBuilder.DropTable(
                name: "feed_cursor");

            migrationBuilder.DropTable(
                name: "wanted_video");

            migrationBuilder.DropTable(
                name: "catalogue_actor");

            migrationBuilder.DropTable(
                name: "catalogue_video");

            migrationBuilder.DropTable(
                name: "catalogue_site");
        }
    }
}
