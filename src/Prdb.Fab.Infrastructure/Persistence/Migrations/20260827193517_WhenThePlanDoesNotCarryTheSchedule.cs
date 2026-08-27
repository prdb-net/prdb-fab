using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhenThePlanDoesNotCarryTheSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PlanShortSince",
                table: "installation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "installation",
                keyColumn: "Id",
                keyValue: 1,
                column: "PlanShortSince",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlanShortSince",
                table: "installation");
        }
    }
}
