using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QRQueue.Migrations
{
    /// <inheritdoc />
    public partial class RestructureToEventModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tickets: 抽選会へのFKを撤去し、参加グループへのFKを新設
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_LotteryGroups_LotteryGroupId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_LotteryGroupId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "LotteryGroupId",
                table: "Tickets");

            migrationBuilder.AddColumn<Guid>(
                name: "ParticipantToken",
                table: "Tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParticipationGroupId",
                table: "Tickets",
                type: "uuid",
                nullable: true);

            // IssueLogs: 列リネーム(データ保持)
            migrationBuilder.RenameColumn(
                name: "LotteryGroupDisplayId",
                table: "IssueLogs",
                newName: "EventDisplayId");

            // LotteryGroups → Events へリネーム(データ保持)
            migrationBuilder.RenameTable(
                name: "LotteryGroups",
                newName: "Events");

            // 制約・インデックス名も新しいテーブル名に合わせる
            migrationBuilder.Sql("ALTER TABLE \"Events\" RENAME CONSTRAINT \"PK_LotteryGroups\" TO \"PK_Events\";");
            migrationBuilder.Sql("ALTER TABLE \"Events\" RENAME CONSTRAINT \"FK_LotteryGroups_TicketInfo_TicketInfoId\" TO \"FK_Events_TicketInfo_TicketInfoId\";");
            migrationBuilder.RenameIndex(
                name: "IX_LotteryGroups_TicketInfoId",
                table: "Events",
                newName: "IX_Events_TicketInfoId");

            // Events: イベント運用状態・マッチング人数
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AutoGroupSize",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            // ParticipationGroups(参加グループ)新設
            migrationBuilder.CreateTable(
                name: "ParticipationGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    JoinToken = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CallCount = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipationGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParticipationGroups_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ParticipationGroupId",
                table: "Tickets",
                column: "ParticipationGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipationGroups_EventId",
                table: "ParticipationGroups",
                column: "EventId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_ParticipationGroups_ParticipationGroupId",
                table: "Tickets",
                column: "ParticipationGroupId",
                principalTable: "ParticipationGroups",
                principalColumn: "Id");

            // 旧TicketStatus(Invalid/PrintPublishing/Valid/Winner/Exchanged)を
            // 新TicketStatus(Registered=0)へ移行
            migrationBuilder.Sql("UPDATE \"Tickets\" SET \"Status\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_ParticipationGroups_ParticipationGroupId",
                table: "Tickets");

            migrationBuilder.DropTable(
                name: "ParticipationGroups");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_ParticipationGroupId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ParticipantToken",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ParticipationGroupId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AutoGroupSize",
                table: "Events");

            migrationBuilder.RenameColumn(
                name: "EventDisplayId",
                table: "IssueLogs",
                newName: "LotteryGroupDisplayId");

            // Events → LotteryGroups へ戻す
            migrationBuilder.RenameTable(
                name: "Events",
                newName: "LotteryGroups");

            migrationBuilder.Sql("ALTER TABLE \"LotteryGroups\" RENAME CONSTRAINT \"PK_Events\" TO \"PK_LotteryGroups\";");
            migrationBuilder.Sql("ALTER TABLE \"LotteryGroups\" RENAME CONSTRAINT \"FK_Events_TicketInfo_TicketInfoId\" TO \"FK_LotteryGroups_TicketInfo_TicketInfoId\";");
            migrationBuilder.RenameIndex(
                name: "IX_Events_TicketInfoId",
                table: "LotteryGroups",
                newName: "IX_LotteryGroups_TicketInfoId");

            migrationBuilder.AddColumn<Guid>(
                name: "LotteryGroupId",
                table: "Tickets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_LotteryGroupId",
                table: "Tickets",
                column: "LotteryGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_LotteryGroups_LotteryGroupId",
                table: "Tickets",
                column: "LotteryGroupId",
                principalTable: "LotteryGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
