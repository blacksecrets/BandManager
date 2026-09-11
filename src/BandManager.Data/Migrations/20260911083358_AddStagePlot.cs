using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStagePlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StagePlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagePlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagePlots_Acts_ActId",
                        column: x => x.ActId,
                        principalTable: "Acts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StagePlotItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StagePlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    BandGearItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisibleId = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    Rotation = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagePlotItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagePlotItems_BandGearItems_BandGearItemId",
                        column: x => x.BandGearItemId,
                        principalTable: "BandGearItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StagePlotItems_StagePlots_StagePlotId",
                        column: x => x.StagePlotId,
                        principalTable: "StagePlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StagePlotItems_BandGearItemId",
                table: "StagePlotItems",
                column: "BandGearItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StagePlotItems_StagePlotId",
                table: "StagePlotItems",
                column: "StagePlotId");

            migrationBuilder.CreateIndex(
                name: "IX_StagePlots_ActId",
                table: "StagePlots",
                column: "ActId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StagePlotItems");

            migrationBuilder.DropTable(
                name: "StagePlots");
        }
    }
}
