using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectedIndexerBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HeadLastSucceededAt",
                table: "indexer_walk_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeadPagesPerPoll",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HeadQueriesSpentToday",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HistoryQueriesSpentToday",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinimumPassQueries",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinimumQueriesSpentToday",
                table: "indexer_walk_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeadLastSucceededAt",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "HeadPagesPerPoll",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "HeadQueriesSpentToday",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "HistoryQueriesSpentToday",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "MinimumPassQueries",
                table: "indexer_walk_state");

            migrationBuilder.DropColumn(
                name: "MinimumQueriesSpentToday",
                table: "indexer_walk_state");
        }
    }
}
