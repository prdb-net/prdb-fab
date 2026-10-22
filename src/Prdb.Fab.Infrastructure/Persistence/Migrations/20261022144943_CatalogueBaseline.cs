using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BaselineBefore",
                table: "recent_window_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BaselineChangesSince",
                table: "recent_window_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaselineCursor",
                table: "recent_window_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaselineDocuments",
                table: "recent_window_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BaselineExceptionalCursor",
                table: "recent_window_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BaselineExceptionalVideoId",
                table: "recent_window_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BaselineFrom",
                table: "recent_window_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaselinePages",
                table: "recent_window_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BaselinePhase",
                table: "recent_window_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "recent_window_state",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BaselineBefore", "BaselineChangesSince", "BaselineCursor", "BaselineDocuments", "BaselineExceptionalCursor", "BaselineExceptionalVideoId", "BaselineFrom", "BaselinePages", "BaselinePhase" },
                values: new object[] { null, null, null, 0, null, null, null, 0, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaselineBefore",
                table: "recent_window_state");

            migrationBuilder.DropColumn(
                name: "BaselineChangesSince",
                table: "recent_window_state");

            migrationBuilder.DropColumn(
                name: "BaselineCursor",
                table: "recent_window_state");

            migrationBuilder.DropColumn(
                name: "BaselineDocuments",
                table: "recent_window_state");

            migrationBuilder.DropColumn(
                name: "BaselineExceptionalCursor",
                table: "recent_window_state");

            migrationBuilder.DropColumn(
                name: "BaselineExceptionalVideoId",
                table: "recent_window_state");

            migrationBuilder.DropColumn(
                name: "BaselineFrom",
                table: "recent_window_state");

            migrationBuilder.DropColumn(
                name: "BaselinePages",
                table: "recent_window_state");

            migrationBuilder.DropColumn(
                name: "BaselinePhase",
                table: "recent_window_state");
        }
    }
}
