using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRehearsalGigLinkAndGigSetFloating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Agenda",
                table: "Rehearsals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FloatingSetlistRef",
                table: "Rehearsals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GigRef",
                table: "Rehearsals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Rehearsals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFloating",
                table: "GigSets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "GigSets",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Agenda",
                table: "Rehearsals");

            migrationBuilder.DropColumn(
                name: "FloatingSetlistRef",
                table: "Rehearsals");

            migrationBuilder.DropColumn(
                name: "GigRef",
                table: "Rehearsals");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Rehearsals");

            migrationBuilder.DropColumn(
                name: "IsFloating",
                table: "GigSets");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "GigSets");
        }
    }
}
