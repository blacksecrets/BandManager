using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBandLocationVenueId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VenueId",
                table: "BandLocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BandLocations_VenueId",
                table: "BandLocations",
                column: "VenueId");

            migrationBuilder.AddForeignKey(
                name: "FK_BandLocations_Venues_VenueId",
                table: "BandLocations",
                column: "VenueId",
                principalTable: "Venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BandLocations_Venues_VenueId",
                table: "BandLocations");

            migrationBuilder.DropIndex(
                name: "IX_BandLocations_VenueId",
                table: "BandLocations");

            migrationBuilder.DropColumn(
                name: "VenueId",
                table: "BandLocations");
        }
    }
}
