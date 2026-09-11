using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTechRiderMonitorMix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TechRiderMonitorMixes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<string>(type: "text", nullable: false),
                    MixDescription = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechRiderMonitorMixes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TechRiderMonitorMixes_Acts_ActId",
                        column: x => x.ActId,
                        principalTable: "Acts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TechRiderMonitorMixes_ActId",
                table: "TechRiderMonitorMixes",
                column: "ActId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TechRiderMonitorMixes");
        }
    }
}
