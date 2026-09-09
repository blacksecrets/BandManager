using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGigSelectedFlyer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SelectedFlyerId",
                table: "Gigs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Gigs_SelectedFlyerId",
                table: "Gigs",
                column: "SelectedFlyerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Gigs_Flyers_SelectedFlyerId",
                table: "Gigs",
                column: "SelectedFlyerId",
                principalTable: "Flyers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gigs_Flyers_SelectedFlyerId",
                table: "Gigs");

            migrationBuilder.DropIndex(
                name: "IX_Gigs_SelectedFlyerId",
                table: "Gigs");

            migrationBuilder.DropColumn(
                name: "SelectedFlyerId",
                table: "Gigs");
        }
    }
}
