using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

[Migration("20260220150000_AddTimeZoneAndDateOrderToAdvancedSettings")]
public partial class AddTimeZoneAndDateOrderToAdvancedSettings : Migration
{
	private readonly IRelationalConnection _connection;

	public AddTimeZoneAndDateOrderToAdvancedSettings()
	{
		_connection = null!;
	}

	public AddTimeZoneAndDateOrderToAdvancedSettings(IRelationalConnection connection)
	{
		_connection = connection;
	}

	protected override void Up(MigrationBuilder migrationBuilder)
	{
		if (!TryAddColumnViaConnection("TimeZoneId", "TEXT NULL"))
			migrationBuilder.AddColumn<string>(name: "TimeZoneId", table: "AdvancedSettings", type: "TEXT", nullable: true);

		if (!TryAddColumnViaConnection("DateOrder", "TEXT NULL"))
			migrationBuilder.AddColumn<string>(name: "DateOrder", table: "AdvancedSettings", type: "TEXT", nullable: true);
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
			return true; // already exists
		}
		finally
		{
			if (!wasOpen)
				_connection.Close();
		}
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn(name: "TimeZoneId", table: "AdvancedSettings");
		migrationBuilder.DropColumn(name: "DateOrder", table: "AdvancedSettings");
	}
}
