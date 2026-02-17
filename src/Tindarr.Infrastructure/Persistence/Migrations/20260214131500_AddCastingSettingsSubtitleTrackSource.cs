using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

public partial class AddCastingSettingsSubtitleTrackSource : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<string>(
			name: "PreferredSubtitleTrackSource",
			table: "casting_settings",
			type: "TEXT",
			nullable: true);

		migrationBuilder.AddColumn<string>(
			name: "SubtitleTrackSourceFallback",
			table: "casting_settings",
			type: "TEXT",
			nullable: true);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(name: "PreferredSubtitleTrackSource", table: "casting_settings");
		migrationBuilder.DropColumn(name: "SubtitleTrackSourceFallback", table: "casting_settings");
	}
}
