using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteChangeStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountHandle",
                table: "catalogue_site",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformId",
                table: "catalogue_site",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformKey",
                table: "catalogue_site",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformTitle",
                table: "catalogue_site",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "catalogue_site",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Url",
                table: "catalogue_site",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountHandle",
                table: "catalogue_site");

            migrationBuilder.DropColumn(
                name: "PlatformId",
                table: "catalogue_site");

            migrationBuilder.DropColumn(
                name: "PlatformKey",
                table: "catalogue_site");

            migrationBuilder.DropColumn(
                name: "PlatformTitle",
                table: "catalogue_site");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "catalogue_site");

            migrationBuilder.DropColumn(
                name: "Url",
                table: "catalogue_site");
        }
    }
}
