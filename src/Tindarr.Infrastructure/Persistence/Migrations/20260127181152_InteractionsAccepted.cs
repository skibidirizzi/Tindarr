using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InteractionsAccepted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accepted_movies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    AcceptedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accepted_movies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "interactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accepted_movies_ServiceType_ServerId_AcceptedAtUtc",
                table: "accepted_movies",
                columns: new[] { "ServiceType", "ServerId", "AcceptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_accepted_movies_ServiceType_ServerId_TmdbId",
                table: "accepted_movies",
                columns: new[] { "ServiceType", "ServerId", "TmdbId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_interactions_UserId_ServiceType_ServerId_CreatedAtUtc",
                table: "interactions",
                columns: new[] { "UserId", "ServiceType", "ServerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_interactions_UserId_ServiceType_ServerId_TmdbId",
                table: "interactions",
                columns: new[] { "UserId", "ServiceType", "ServerId", "TmdbId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accepted_movies");

            migrationBuilder.DropTable(
                name: "interactions");
        }
    }
}
