using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBandMembershipLastSelectedGig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSelectedGigRef",
                table: "BandMemberships",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSelectedGigRef",
                table: "BandMemberships");
        }
    }
}
