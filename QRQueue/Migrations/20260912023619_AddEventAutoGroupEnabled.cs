using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QRQueue.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAutoGroupEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoGroupEnabled",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoGroupEnabled",
                table: "Events");
        }
    }
}
