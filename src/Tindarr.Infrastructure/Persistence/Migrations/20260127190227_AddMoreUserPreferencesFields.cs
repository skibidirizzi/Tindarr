using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoreUserPreferencesFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExcludedGenresJson",
                table: "user_preferences",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<double>(
                name: "MaxRating",
                table: "user_preferences",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MinRating",
                table: "user_preferences",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredRegionsJson",
                table: "user_preferences",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "SortBy",
                table: "user_preferences",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "popularity.desc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedGenresJson",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "MaxRating",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "MinRating",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "PreferredRegionsJson",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "SortBy",
                table: "user_preferences");
        }
    }
}
