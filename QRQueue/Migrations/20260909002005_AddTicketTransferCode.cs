using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QRQueue.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketTransferCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TransferCodeExpiresAt",
                table: "Tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferCodeHash",
                table: "Tickets",
                type: "text",
                nullable: true);

            // 引き継ぎコードの照合(complete時の頻用パス)をインデックスで高速化
            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TransferCodeHash",
                table: "Tickets",
                column: "TransferCodeHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_TransferCodeHash",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "TransferCodeExpiresAt",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "TransferCodeHash",
                table: "Tickets");
        }
    }
}
