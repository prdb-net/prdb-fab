using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublishGeneratedPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PreviewPublicationExplainedAt",
                table: "installation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PublishGeneratedPreviews",
                table: "installation",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.UpdateData(
                table: "installation",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PreviewPublicationExplainedAt", "PublishGeneratedPreviews" },
                values: new object[] { null, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviewPublicationExplainedAt",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "PublishGeneratedPreviews",
                table: "installation");
        }
    }
}
