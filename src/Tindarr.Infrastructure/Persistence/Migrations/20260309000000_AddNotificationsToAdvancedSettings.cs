using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

[Migration("20260309000000_AddNotificationsToAdvancedSettings")]
public partial class AddNotificationsToAdvancedSettings : Migration
{
	private readonly IRelationalConnection _connection;

	public AddNotificationsToAdvancedSettings()
	{
		_connection = null!;
	}

	public AddNotificationsToAdvancedSettings(IRelationalConnection connection)
	{
		_connection = connection;
	}

	protected override void Up(MigrationBuilder migrationBuilder)
	{
		if (TryAddColumnViaConnection("NotificationsEnabled", "INTEGER NULL")
			& TryAddColumnViaConnection("NotificationsWebhookUrlsJson", "TEXT NULL")
			& TryAddColumnViaConnection("NotificationsEventsMask", "INTEGER NULL"))
		{
			return;
		}

		migrationBuilder.AddColumn<bool>(
			name: "NotificationsEnabled",
			table: "AdvancedSettings",
			type: "INTEGER",
			nullable: true);

		migrationBuilder.AddColumn<string>(
			name: "NotificationsWebhookUrlsJson",
			table: "AdvancedSettings",
			type: "TEXT",
			nullable: true);

		migrationBuilder.AddColumn<int>(
			name: "NotificationsEventsMask",
			table: "AdvancedSettings",
			type: "INTEGER",
			nullable: true);
	}

	private bool TryAddColumnViaConnection(string columnName, string columnDef)
	{
		if (_connection?.DbConnection is not SqliteConnection sqlite)
			return false;

		var wasOpen = sqlite.State == System.Data.ConnectionState.Open;
		if (!wasOpen)
			_connection.Open();

		try
		{
			using var cmd = sqlite.CreateCommand();
			cmd.CommandText = $"ALTER TABLE AdvancedSettings ADD COLUMN {columnName} {columnDef};";
			cmd.ExecuteNonQuery();
			return true;
		}
		catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		finally
		{
			if (!wasOpen)
				_connection.Close();
		}
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(
			name: "NotificationsEnabled",
			table: "AdvancedSettings");

		migrationBuilder.DropColumn(
			name: "NotificationsWebhookUrlsJson",
			table: "AdvancedSettings");

		migrationBuilder.DropColumn(
			name: "NotificationsEventsMask",
			table: "AdvancedSettings");
	}
}
