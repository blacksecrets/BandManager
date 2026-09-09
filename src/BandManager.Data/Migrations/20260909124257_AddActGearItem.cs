using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActGearItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActGearItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActId = table.Column<Guid>(type: "uuid", nullable: false),
                    BandGearItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActGearItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActGearItems_Acts_ActId",
                        column: x => x.ActId,
                        principalTable: "Acts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActGearItems_BandGearItems_BandGearItemId",
                        column: x => x.BandGearItemId,
                        principalTable: "BandGearItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActGearItems_ActId_BandGearItemId",
                table: "ActGearItems",
                columns: new[] { "ActId", "BandGearItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActGearItems_BandGearItemId",
                table: "ActGearItems",
                column: "BandGearItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActGearItems");
        }
    }
}
