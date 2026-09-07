using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class CadenceAssignees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Owner",
                table: "ScheduleItems");

            migrationBuilder.DropColumn(
                name: "Owner",
                table: "CadenceRules");

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeUserId1",
                table: "ScheduleItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeUserId2",
                table: "ScheduleItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeUserId1",
                table: "CadenceRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeUserId2",
                table: "CadenceRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleItems_AssigneeUserId1",
                table: "ScheduleItems",
                column: "AssigneeUserId1");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleItems_AssigneeUserId2",
                table: "ScheduleItems",
                column: "AssigneeUserId2");

            migrationBuilder.CreateIndex(
                name: "IX_CadenceRules_AssigneeUserId1",
                table: "CadenceRules",
                column: "AssigneeUserId1");

            migrationBuilder.CreateIndex(
                name: "IX_CadenceRules_AssigneeUserId2",
                table: "CadenceRules",
                column: "AssigneeUserId2");

            migrationBuilder.AddForeignKey(
                name: "FK_CadenceRules_AspNetUsers_AssigneeUserId1",
                table: "CadenceRules",
                column: "AssigneeUserId1",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CadenceRules_AspNetUsers_AssigneeUserId2",
                table: "CadenceRules",
                column: "AssigneeUserId2",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduleItems_AspNetUsers_AssigneeUserId1",
                table: "ScheduleItems",
                column: "AssigneeUserId1",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduleItems_AspNetUsers_AssigneeUserId2",
                table: "ScheduleItems",
                column: "AssigneeUserId2",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CadenceRules_AspNetUsers_AssigneeUserId1",
                table: "CadenceRules");

            migrationBuilder.DropForeignKey(
                name: "FK_CadenceRules_AspNetUsers_AssigneeUserId2",
                table: "CadenceRules");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduleItems_AspNetUsers_AssigneeUserId1",
                table: "ScheduleItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduleItems_AspNetUsers_AssigneeUserId2",
                table: "ScheduleItems");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleItems_AssigneeUserId1",
                table: "ScheduleItems");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleItems_AssigneeUserId2",
                table: "ScheduleItems");

            migrationBuilder.DropIndex(
                name: "IX_CadenceRules_AssigneeUserId1",
                table: "CadenceRules");

            migrationBuilder.DropIndex(
                name: "IX_CadenceRules_AssigneeUserId2",
                table: "CadenceRules");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId1",
                table: "ScheduleItems");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId2",
                table: "ScheduleItems");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId1",
                table: "CadenceRules");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId2",
                table: "CadenceRules");

            migrationBuilder.AddColumn<string>(
                name: "Owner",
                table: "ScheduleItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Owner",
                table: "CadenceRules",
                type: "text",
                nullable: true);
        }
    }
}
