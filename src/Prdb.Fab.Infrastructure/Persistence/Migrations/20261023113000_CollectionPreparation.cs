using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CollectionPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SuppliedWanted",
                table: "automation_rule",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "IndexerId",
                table: "arriving_file",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<Guid>(
                name: "DownloadId",
                table: "arriving_file",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.CreateTable(
                name: "autopilot_baseline",
                columns: table => new
                {
                    UserHash = table.Column<string>(type: "TEXT", nullable: false),
                    BaselineAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_autopilot_baseline", x => x.UserHash);
                });

            migrationBuilder.CreateTable(
                name: "autopilot_membership",
                columns: table => new
                {
                    UserHash = table.Column<string>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WantedSinceAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Admitted = table.Column<bool>(type: "INTEGER", nullable: false),
                    InitialBatch = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_autopilot_membership", x => new { x.UserHash, x.VideoId });
                });

            migrationBuilder.CreateTable(
                name: "collection_file",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Probed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeftAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlannedPath = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedEvidence = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_file", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "collection_preparation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Phase = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_preparation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "collection_root",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    IsLibrary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_root", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "collection_walk",
                columns: table => new
                {
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    RootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AfterName = table.Column<string>(type: "TEXT", nullable: true),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_walk", x => x.Path);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collection_file_Probed_LeftAt",
                table: "collection_file",
                columns: new[] { "Probed", "LeftAt" });

            migrationBuilder.CreateIndex(
                name: "IX_collection_file_SourcePath",
                table: "collection_file",
                column: "SourcePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_root_Path",
                table: "collection_root",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autopilot_baseline");

            migrationBuilder.DropTable(
                name: "autopilot_membership");

            migrationBuilder.DropTable(
                name: "collection_file");

            migrationBuilder.DropTable(
                name: "collection_preparation");

            migrationBuilder.DropTable(
                name: "collection_root");

            migrationBuilder.DropTable(
                name: "collection_walk");

            migrationBuilder.DropColumn(
                name: "SuppliedWanted",
                table: "automation_rule");

            migrationBuilder.AlterColumn<Guid>(
                name: "IndexerId",
                table: "arriving_file",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "DownloadId",
                table: "arriving_file",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
