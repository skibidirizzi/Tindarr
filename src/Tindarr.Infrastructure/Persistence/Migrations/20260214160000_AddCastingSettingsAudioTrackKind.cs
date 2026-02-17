using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

public partial class AddCastingSettingsAudioTrackKind : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<string>(
			name: "PreferredAudioTrackKind",
			table: "casting_settings",
			type: "TEXT",
			nullable: true);

		migrationBuilder.AddColumn<string>(
			name: "AudioTrackKindFallback",
			table: "casting_settings",
			type: "TEXT",
			nullable: true);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(name: "PreferredAudioTrackKind", table: "casting_settings");
		migrationBuilder.DropColumn(name: "AudioTrackKindFallback", table: "casting_settings");
	}
}
