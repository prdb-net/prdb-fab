using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StatusSurface : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeferredUntil",
                table: "routine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDeferredAt",
                table: "routine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastDeferredReason",
                table: "routine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRunNowAt",
                table: "routine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRunNowDetail",
                table: "routine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRunNowOutcome",
                table: "routine",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RunNowPending",
                table: "routine",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeferredUntil",
                table: "routine");

            migrationBuilder.DropColumn(
                name: "LastDeferredAt",
                table: "routine");

            migrationBuilder.DropColumn(
                name: "LastDeferredReason",
                table: "routine");

            migrationBuilder.DropColumn(
                name: "LastRunNowAt",
                table: "routine");

            migrationBuilder.DropColumn(
                name: "LastRunNowDetail",
                table: "routine");

            migrationBuilder.DropColumn(
                name: "LastRunNowOutcome",
                table: "routine");

            migrationBuilder.DropColumn(
                name: "RunNowPending",
                table: "routine");
        }
    }
}
