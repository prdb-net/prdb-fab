using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Available",
                table: "catalogue_video",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "catalogue_video",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoId",
                table: "catalogue_video",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Available",
                table: "catalogue_video");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "catalogue_video");

            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                table: "catalogue_video");
        }
    }
}
