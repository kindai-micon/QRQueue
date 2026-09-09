using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QRQueue.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupCoJoinAndDraftStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowCoJoin",
                table: "ParticipationGroups",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowCoJoin",
                table: "ParticipationGroups");
        }
    }
}
