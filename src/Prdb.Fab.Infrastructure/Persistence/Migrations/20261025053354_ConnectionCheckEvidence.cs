using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionCheckEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrdbLastCheckOutcome",
                table: "installation",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbLastCheckedAt",
                table: "installation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrdbLastSuccessAt",
                table: "installation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SabnzbdLastCheckOutcome",
                table: "installation",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SabnzbdLastCheckedAt",
                table: "installation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SabnzbdLastSuccessAt",
                table: "installation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSuccessAt",
                table: "indexer",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "installation",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PrdbLastCheckOutcome", "PrdbLastCheckedAt", "PrdbLastSuccessAt", "SabnzbdLastCheckOutcome", "SabnzbdLastCheckedAt", "SabnzbdLastSuccessAt" },
                values: new object[] { null, null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrdbLastCheckOutcome",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "PrdbLastCheckedAt",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "PrdbLastSuccessAt",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "SabnzbdLastCheckOutcome",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "SabnzbdLastCheckedAt",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "SabnzbdLastSuccessAt",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "LastSuccessAt",
                table: "indexer");
        }
    }
}
