using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

[Migration("20260220000000_AddAdvancedSettings")]
public partial class AddAdvancedSettings : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "AdvancedSettings",
			columns: table => new
			{
				Id = table.Column<long>(type: "INTEGER", nullable: false)
					.Annotation("Sqlite:Autoincrement", true),
				ApiRateLimitEnabled = table.Column<bool>(type: "INTEGER", nullable: true),
				ApiRateLimitPermitLimit = table.Column<int>(type: "INTEGER", nullable: true),
				ApiRateLimitWindowMinutes = table.Column<int>(type: "INTEGER", nullable: true),
				CleanupEnabled = table.Column<bool>(type: "INTEGER", nullable: true),
				CleanupIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: true),
				CleanupPurgeGuestUsers = table.Column<bool>(type: "INTEGER", nullable: true),
				CleanupGuestUserMaxAgeHours = table.Column<int>(type: "INTEGER", nullable: true),
				UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_AdvancedSettings", x => x.Id);
			});
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(name: "AdvancedSettings");
	}
}
