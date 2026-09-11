using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBandMembershipLastSelectedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSelectedAt",
                table: "BandMemberships",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSelectedAt",
                table: "BandMemberships");
        }
    }
}
