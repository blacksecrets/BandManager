using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlyersAndCatalogCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "CatalogItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FlyerTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    BackgroundCatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Fields = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlyerTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlyerTemplates_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FlyerTemplates_CatalogItems_BackgroundCatalogItemId",
                        column: x => x.BackgroundCatalogItemId,
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Flyers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedCatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlyerTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    GigRef = table.Column<string>(type: "text", nullable: false),
                    Fields = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flyers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Flyers_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Flyers_CatalogItems_GeneratedCatalogItemId",
                        column: x => x.GeneratedCatalogItemId,
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Flyers_FlyerTemplates_FlyerTemplateId",
                        column: x => x.FlyerTemplateId,
                        principalTable: "FlyerTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Flyers_BandId_GigRef",
                table: "Flyers",
                columns: new[] { "BandId", "GigRef" });

            migrationBuilder.CreateIndex(
                name: "IX_Flyers_FlyerTemplateId",
                table: "Flyers",
                column: "FlyerTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Flyers_GeneratedCatalogItemId",
                table: "Flyers",
                column: "GeneratedCatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FlyerTemplates_BackgroundCatalogItemId",
                table: "FlyerTemplates",
                column: "BackgroundCatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FlyerTemplates_BandId",
                table: "FlyerTemplates",
                column: "BandId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Flyers");

            migrationBuilder.DropTable(
                name: "FlyerTemplates");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "CatalogItems");
        }
    }
}
