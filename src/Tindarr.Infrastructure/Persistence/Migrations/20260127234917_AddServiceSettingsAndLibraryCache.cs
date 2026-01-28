using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceSettingsAndLibraryCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "library_cache",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_cache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "service_settings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    RadarrBaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    RadarrApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    RadarrQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    RadarrRootFolderPath = table.Column<string>(type: "TEXT", nullable: true),
                    RadarrTagLabel = table.Column<string>(type: "TEXT", nullable: true),
                    RadarrTagId = table.Column<int>(type: "INTEGER", nullable: true),
                    RadarrAutoAddEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RadarrLastAutoAddAcceptedId = table.Column<long>(type: "INTEGER", nullable: true),
                    RadarrLastLibrarySyncUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_library_cache_ServiceType_ServerId",
                table: "library_cache",
                columns: new[] { "ServiceType", "ServerId" });

            migrationBuilder.CreateIndex(
                name: "IX_library_cache_ServiceType_ServerId_TmdbId",
                table: "library_cache",
                columns: new[] { "ServiceType", "ServerId", "TmdbId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_settings_ServiceType_ServerId",
                table: "service_settings",
                columns: new[] { "ServiceType", "ServerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library_cache");

            migrationBuilder.DropTable(
                name: "service_settings");
        }
    }
}
