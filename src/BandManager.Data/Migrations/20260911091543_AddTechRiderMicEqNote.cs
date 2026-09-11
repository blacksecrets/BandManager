using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTechRiderMicEqNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TechRiderMicEqNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActId = table.Column<Guid>(type: "uuid", nullable: false),
                    MicModel = table.Column<string>(type: "text", nullable: false),
                    Context = table.Column<string>(type: "text", nullable: true),
                    FrequencyRowsJson = table.Column<string>(type: "text", nullable: false),
                    GeneralNotes = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechRiderMicEqNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TechRiderMicEqNotes_Acts_ActId",
                        column: x => x.ActId,
                        principalTable: "Acts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TechRiderMicEqNotes_ActId",
                table: "TechRiderMicEqNotes",
                column: "ActId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TechRiderMicEqNotes");
        }
    }
}
