using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

public partial class AddCastingSettingsLanguagePreferences : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<string>(
			name: "PreferredSubtitleLanguage",
			table: "casting_settings",
			type: "TEXT",
			nullable: true);

		migrationBuilder.AddColumn<string>(
			name: "SubtitleLanguageFallback",
			table: "casting_settings",
			type: "TEXT",
			nullable: true);

		migrationBuilder.AddColumn<string>(
			name: "PreferredAudioLanguage",
			table: "casting_settings",
			type: "TEXT",
			nullable: true);

		migrationBuilder.AddColumn<string>(
			name: "AudioLanguageFallback",
			table: "casting_settings",
			type: "TEXT",
			nullable: true);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(name: "PreferredSubtitleLanguage", table: "casting_settings");
		migrationBuilder.DropColumn(name: "SubtitleLanguageFallback", table: "casting_settings");
		migrationBuilder.DropColumn(name: "PreferredAudioLanguage", table: "casting_settings");
		migrationBuilder.DropColumn(name: "AudioLanguageFallback", table: "casting_settings");
	}
}
