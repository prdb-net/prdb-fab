using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExactIdentificationInputReuse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentificationInputKey",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_release_IdentificationInputKey_LastIdentifiedAt",
                table: "release",
                columns: new[] { "IdentificationInputKey", "LastIdentifiedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_release_IdentificationInputKey_LastIdentifiedAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "IdentificationInputKey",
                table: "release");
        }
    }
}
