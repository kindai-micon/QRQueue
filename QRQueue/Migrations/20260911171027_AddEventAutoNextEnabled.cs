using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QRQueue.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAutoNextEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoNextEnabled",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoNextEnabled",
                table: "Events");
        }
    }
}
