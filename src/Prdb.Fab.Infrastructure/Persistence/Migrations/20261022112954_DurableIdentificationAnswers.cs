using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DurableIdentificationAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetailRetryAt",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificationAnswerJson",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IdentificationDetailsPending",
                table: "release",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_release_IdentificationDetailsPending_DetailRetryAt",
                table: "release",
                columns: new[] { "IdentificationDetailsPending", "DetailRetryAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_release_IdentificationDetailsPending_DetailRetryAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "DetailRetryAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "IdentificationAnswerJson",
                table: "release");

            migrationBuilder.DropColumn(
                name: "IdentificationDetailsPending",
                table: "release");
        }
    }
}
