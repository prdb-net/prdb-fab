using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Fab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationWebhookUrl",
                table: "installation",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotificationsEnabled",
                table: "installation",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "notification_delivery",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_delivery", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "installation",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "NotificationWebhookUrl", "NotificationsEnabled" },
                values: new object[] { null, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_delivery");

            migrationBuilder.DropColumn(
                name: "NotificationWebhookUrl",
                table: "installation");

            migrationBuilder.DropColumn(
                name: "NotificationsEnabled",
                table: "installation");
        }
    }
}
