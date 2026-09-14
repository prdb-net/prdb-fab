using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliverGeneratedPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ModerationTargetId",
                table: "preview_publication",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrdbImageId",
                table: "preview_publication",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedUnder",
                table: "preview_publication",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_preview_publication_State_GeneratedAt",
                table: "preview_publication",
                columns: new[] { "State", "GeneratedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_preview_publication_State_GeneratedAt",
                table: "preview_publication");

            migrationBuilder.DropColumn(
                name: "ModerationTargetId",
                table: "preview_publication");

            migrationBuilder.DropColumn(
                name: "PrdbImageId",
                table: "preview_publication");

            migrationBuilder.DropColumn(
                name: "SubmittedUnder",
                table: "preview_publication");
        }
    }
}
