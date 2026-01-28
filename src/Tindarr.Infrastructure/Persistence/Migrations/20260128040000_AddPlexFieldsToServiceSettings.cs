using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlexFieldsToServiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlexAuthToken",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlexClientIdentifier",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlexLastLibrarySyncUtc",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlexServerAccessToken",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlexServerName",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PlexServerOnline",
                table: "service_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PlexServerOwned",
                table: "service_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlexServerPlatform",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlexServerUri",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlexServerVersion",
                table: "service_settings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlexAuthToken",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexClientIdentifier",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexLastLibrarySyncUtc",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexServerAccessToken",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexServerName",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexServerOnline",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexServerOwned",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexServerPlatform",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexServerUri",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "PlexServerVersion",
                table: "service_settings");
        }
    }
}
