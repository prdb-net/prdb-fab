using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessAndInstallation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    PrdbApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    PrdbUserHash = table.Column<string>(type: "TEXT", nullable: true),
                    LibraryRoot = table.Column<string>(type: "TEXT", nullable: true),
                    SabnzbdUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SabnzbdApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    SabnzbdCategory = table.Column<string>(type: "TEXT", nullable: true),
                    PathMappingFrom = table.Column<string>(type: "TEXT", nullable: true),
                    PathMappingTo = table.Column<string>(type: "TEXT", nullable: true),
                    OnboardingStep = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installation", x => x.Id);
                    table.CheckConstraint("CK_installation_one_row", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "session",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "installation",
                columns: new[] { "Id", "LibraryRoot", "OnboardingStep", "PasswordHash", "PathMappingFrom", "PathMappingTo", "PrdbApiKey", "PrdbUserHash", "SabnzbdApiKey", "SabnzbdCategory", "SabnzbdUrl" },
                values: new object[] { 1, null, "Password", null, null, null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_session_ExpiresAt",
                table: "session",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_session_TokenHash",
                table: "session",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation");

            migrationBuilder.DropTable(
                name: "session");
        }
    }
}
