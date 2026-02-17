using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tindarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomAndGuestLifetimeMinutesToJoinAddressSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GuestSessionLifetimeMinutes",
                table: "JoinAddressSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoomLifetimeMinutes",
                table: "JoinAddressSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuestSessionLifetimeMinutes",
                table: "JoinAddressSettings");

            migrationBuilder.DropColumn(
                name: "RoomLifetimeMinutes",
                table: "JoinAddressSettings");
        }
    }
}
