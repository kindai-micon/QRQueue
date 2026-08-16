using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QRQueue.Migrations
{
    /// <inheritdoc />
    public partial class DropLotterySlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_LotterySlots_LotterySlotsId",
                table: "Tickets");

            migrationBuilder.DropTable(
                name: "LotterySlots");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_LotterySlotsId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "LotterySlotsId",
                table: "Tickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LotterySlotsId",
                table: "Tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LotterySlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LotteryGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadLine = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisplayId = table.Column<Guid>(type: "uuid", nullable: false),
                    Merchandise = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NumberOfFrames = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotterySlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotterySlots_LotteryGroups_LotteryGroupId",
                        column: x => x.LotteryGroupId,
                        principalTable: "LotteryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_LotterySlotsId",
                table: "Tickets",
                column: "LotterySlotsId");

            migrationBuilder.CreateIndex(
                name: "IX_LotterySlots_LotteryGroupId",
                table: "LotterySlots",
                column: "LotteryGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_LotterySlots_LotterySlotsId",
                table: "Tickets",
                column: "LotterySlotsId",
                principalTable: "LotterySlots",
                principalColumn: "Id");
        }
    }
}
