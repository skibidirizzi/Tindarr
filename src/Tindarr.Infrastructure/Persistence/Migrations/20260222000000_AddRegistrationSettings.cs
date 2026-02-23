using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

[Migration("20260222000000_AddRegistrationSettings")]
public partial class AddRegistrationSettings : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable(
			name: "RegistrationSettings",
			columns: table => new
			{
				Id = table.Column<long>(type: "INTEGER", nullable: false)
					.Annotation("Sqlite:Autoincrement", true),
				AllowOpenRegistration = table.Column<bool>(type: "INTEGER", nullable: true),
				RequireAdminApprovalForNewUsers = table.Column<bool>(type: "INTEGER", nullable: true),
				DefaultRole = table.Column<string>(type: "TEXT", nullable: true),
				UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
			},
			constraints: table =>
			{
				table.PrimaryKey("PK_RegistrationSettings", x => x.Id);
			});
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable(name: "RegistrationSettings");
	}
}
