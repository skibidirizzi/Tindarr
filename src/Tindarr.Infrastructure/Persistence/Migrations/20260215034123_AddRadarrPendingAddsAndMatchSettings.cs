using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRadarrPendingAddsAndMatchSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MatchMinUserPercent",
                table: "service_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatchMinUsers",
                table: "service_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RadarrAutoAddIntervalMinutes",
                table: "service_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RadarrLastAutoAddRunUtc",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "radarr_pending_adds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReadyAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CanceledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_radarr_pending_adds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_radarr_pending_adds_ServiceType_ServerId_ReadyAtUtc",
                table: "radarr_pending_adds",
                columns: new[] { "ServiceType", "ServerId", "ReadyAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_radarr_pending_adds_ServiceType_ServerId_UserId_TmdbId",
                table: "radarr_pending_adds",
                columns: new[] { "ServiceType", "ServerId", "UserId", "TmdbId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "radarr_pending_adds");

            migrationBuilder.DropColumn(
                name: "MatchMinUserPercent",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "MatchMinUsers",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "RadarrAutoAddIntervalMinutes",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "RadarrLastAutoAddRunUtc",
                table: "service_settings");
        }
    }
}
