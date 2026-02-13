using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJellyfinEmbyFieldsToServiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmbyApiKey",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbyBaseUrl",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmbyLastLibrarySyncUtc",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbyServerName",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbyServerVersion",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JellyfinApiKey",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JellyfinBaseUrl",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "JellyfinLastLibrarySyncUtc",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JellyfinServerName",
                table: "service_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JellyfinServerVersion",
                table: "service_settings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbyApiKey",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "EmbyBaseUrl",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "EmbyLastLibrarySyncUtc",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "EmbyServerName",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "EmbyServerVersion",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "JellyfinApiKey",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "JellyfinBaseUrl",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "JellyfinLastLibrarySyncUtc",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "JellyfinServerName",
                table: "service_settings");

            migrationBuilder.DropColumn(
                name: "JellyfinServerVersion",
                table: "service_settings");
        }
    }
}
