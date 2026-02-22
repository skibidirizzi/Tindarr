using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

[Migration("20260220180000_AddTmdbReadAccessTokenToAdvancedSettings")]
public partial class AddTmdbReadAccessTokenToAdvancedSettings : Migration
{
	private readonly IRelationalConnection _connection;

	public AddTmdbReadAccessTokenToAdvancedSettings()
	{
		_connection = null!;
	}

	public AddTmdbReadAccessTokenToAdvancedSettings(IRelationalConnection connection)
	{
		_connection = connection;
	}

	protected override void Up(MigrationBuilder migrationBuilder)
	{
		if (TryAddColumnViaConnection("TmdbReadAccessToken", "TEXT NULL"))
			return;

		migrationBuilder.AddColumn<string>(
			name: "TmdbReadAccessToken",
			table: "AdvancedSettings",
			type: "TEXT",
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
			name: "TmdbReadAccessToken",
			table: "AdvancedSettings");
	}
}
