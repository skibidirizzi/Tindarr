using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

public partial class AddCastingSettings : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "casting_settings",
			columns: table => new
			{
				Id = table.Column<long>(type: "INTEGER", nullable: false)
					.Annotation("Sqlite:Autoincrement", true),
				PreferredSubtitleSource = table.Column<string>(type: "TEXT", nullable: true),
				SubtitleFallback = table.Column<string>(type: "TEXT", nullable: true),
				PreferredAudioStyle = table.Column<string>(type: "TEXT", nullable: true),
				AudioFallback = table.Column<string>(type: "TEXT", nullable: true),
				UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_casting_settings", x => x.Id);
			});
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(name: "casting_settings");
	}
}
