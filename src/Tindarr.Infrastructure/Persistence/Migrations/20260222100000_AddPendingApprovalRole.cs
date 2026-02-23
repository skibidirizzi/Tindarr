using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations;

[Migration("20260222100000_AddPendingApprovalRole")]
public partial class AddPendingApprovalRole : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.InsertData(
			table: "roles",
			columns: new[] { "Name", "CreatedAtUtc" },
			values: new object[,]
			{
				{ "PendingApproval", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero) }
			});
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DeleteData(
			table: "roles",
			keyColumn: "Name",
			keyValue: "PendingApproval");
	}
}
