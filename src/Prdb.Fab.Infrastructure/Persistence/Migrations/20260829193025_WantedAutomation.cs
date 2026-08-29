using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WantedAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AutomationDecisionAt",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutomationDecisionReason",
                table: "release",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutomationPending",
                table: "release",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutomaticDownloadCap",
                table: "installation",
                type: "INTEGER",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.CreateTable(
                name: "automation_rule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimumSize = table.Column<long>(type: "INTEGER", nullable: true),
                    MaximumSize = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_rule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "automation_rule_indexer",
                columns: table => new
                {
                    AutomationRuleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IndexerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_rule_indexer", x => new { x.AutomationRuleId, x.IndexerId });
                    table.ForeignKey(
                        name: "FK_automation_rule_indexer_automation_rule_AutomationRuleId",
                        column: x => x.AutomationRuleId,
                        principalTable: "automation_rule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_automation_rule_indexer_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "download_origin_rule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutomationRuleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RuleName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_origin_rule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_download_origin_rule_automation_rule_AutomationRuleId",
                        column: x => x.AutomationRuleId,
                        principalTable: "automation_rule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_download_origin_rule_download_DownloadId",
                        column: x => x.DownloadId,
                        principalTable: "download",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "gate_admission",
                columns: new[] { "Confidence", "Gate" },
                values: new object[,]
                {
                    { "Exact", "BeforeDownload" },
                    { "Probable", "BeforeDownload" },
                    { "Strong", "BeforeDownload" }
                });

            migrationBuilder.UpdateData(
                table: "installation",
                keyColumn: "Id",
                keyValue: 1,
                column: "AutomaticDownloadCap",
                value: 20);

            migrationBuilder.CreateIndex(
                name: "IX_release_AutomationPending",
                table: "release",
                column: "AutomationPending",
                filter: "\"AutomationPending\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_automation_rule_Name",
                table: "automation_rule",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_automation_rule_indexer_IndexerId",
                table: "automation_rule_indexer",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_download_origin_rule_AutomationRuleId",
                table: "download_origin_rule",
                column: "AutomationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_download_origin_rule_DownloadId",
                table: "download_origin_rule",
                column: "DownloadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_rule_indexer");

            migrationBuilder.DropTable(
                name: "download_origin_rule");

            migrationBuilder.DropTable(
                name: "automation_rule");

            migrationBuilder.DropIndex(
                name: "IX_release_AutomationPending",
                table: "release");

            migrationBuilder.DeleteData(
                table: "gate_admission",
                keyColumns: new[] { "Confidence", "Gate" },
                keyValues: new object[] { "Exact", "BeforeDownload" });

            migrationBuilder.DeleteData(
                table: "gate_admission",
                keyColumns: new[] { "Confidence", "Gate" },
                keyValues: new object[] { "Probable", "BeforeDownload" });

            migrationBuilder.DeleteData(
                table: "gate_admission",
                keyColumns: new[] { "Confidence", "Gate" },
                keyValues: new object[] { "Strong", "BeforeDownload" });

            migrationBuilder.DropColumn(
                name: "AutomationDecisionAt",
                table: "release");

            migrationBuilder.DropColumn(
                name: "AutomationDecisionReason",
                table: "release");

            migrationBuilder.DropColumn(
                name: "AutomationPending",
                table: "release");

            migrationBuilder.DropColumn(
                name: "AutomaticDownloadCap",
                table: "installation");
        }
    }
}
