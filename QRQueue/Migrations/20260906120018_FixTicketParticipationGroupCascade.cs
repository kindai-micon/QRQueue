using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QRQueue.Migrations
{
    /// <inheritdoc />
    public partial class FixTicketParticipationGroupCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_ParticipationGroups_ParticipationGroupId",
                table: "Tickets");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_ParticipationGroups_ParticipationGroupId",
                table: "Tickets",
                column: "ParticipationGroupId",
                principalTable: "ParticipationGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_ParticipationGroups_ParticipationGroupId",
                table: "Tickets");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_ParticipationGroups_ParticipationGroupId",
                table: "Tickets",
                column: "ParticipationGroupId",
                principalTable: "ParticipationGroups",
                principalColumn: "Id");
        }
    }
}
