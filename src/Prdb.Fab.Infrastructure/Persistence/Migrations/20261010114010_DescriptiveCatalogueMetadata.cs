using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DescriptiveCatalogueMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "catalogue_video",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StashdbSceneId",
                table: "catalogue_video",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DetailUpdatedAtUtc",
                table: "catalogue_site",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinksJson",
                table: "catalogue_site",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "catalogue_video");

            migrationBuilder.DropColumn(
                name: "StashdbSceneId",
                table: "catalogue_video");

            migrationBuilder.DropColumn(
                name: "DetailUpdatedAtUtc",
                table: "catalogue_site");

            migrationBuilder.DropColumn(
                name: "LinksJson",
                table: "catalogue_site");
        }
    }
}
