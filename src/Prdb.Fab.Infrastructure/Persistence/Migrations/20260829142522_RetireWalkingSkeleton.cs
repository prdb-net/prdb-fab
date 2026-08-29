using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireWalkingSkeleton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The routine log cascades with its row. Leaving either behind
            // would keep a retired diagnostic surface visible in schedule
            // state after the code that can run it has gone.
            migrationBuilder.Sql("""
                DELETE FROM "routine"
                WHERE "Name" = 'skeleton-sweep';
                """);

            migrationBuilder.DropTable(
                name: "skeleton_item");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "skeleton_item",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    SweptAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skeleton_item", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_skeleton_item_SweptAt",
                table: "skeleton_item",
                column: "SweptAt");
        }
    }
}
