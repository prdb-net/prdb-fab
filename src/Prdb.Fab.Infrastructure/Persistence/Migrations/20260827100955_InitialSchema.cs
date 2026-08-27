using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "routine",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: true),
                    Lane = table.Column<string>(type: "TEXT", nullable: false),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routine", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "skeleton_item",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SweptAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skeleton_item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "routine_run",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoutineId = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    ItemsHandled = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routine_run", x => x.Id);
                    table.ForeignKey(
                        name: "FK_routine_run_routine_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "routine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_routine_Lane_DueAt",
                table: "routine",
                columns: new[] { "Lane", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_routine_Name_Target",
                table: "routine",
                columns: new[] { "Name", "Target" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_routine_run_RoutineId_StartedAt",
                table: "routine_run",
                columns: new[] { "RoutineId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_skeleton_item_SweptAt",
                table: "skeleton_item",
                column: "SweptAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routine_run");

            migrationBuilder.DropTable(
                name: "skeleton_item");

            migrationBuilder.DropTable(
                name: "routine");
        }
    }
}
