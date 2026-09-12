using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationBandId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BandId",
                table: "Notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_BandId",
                table: "Notifications",
                column: "BandId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Bands_BandId",
                table: "Notifications",
                column: "BandId",
                principalTable: "Bands",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Bands_BandId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_BandId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "BandId",
                table: "Notifications");
        }
    }
}
