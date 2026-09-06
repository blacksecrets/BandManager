using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBandArchiveAndBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundPath",
                table: "Bands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaviconPath",
                table: "Bands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Bands",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Bands",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundPath",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "FaviconPath",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Bands");
        }
    }
}
