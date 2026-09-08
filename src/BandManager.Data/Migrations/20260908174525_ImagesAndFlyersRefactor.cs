using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImagesAndFlyersRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Flyers_FlyerTemplates_FlyerTemplateId",
                table: "Flyers");

            migrationBuilder.DropTable(
                name: "FlyerTemplates");

            migrationBuilder.RenameColumn(
                name: "FlyerTemplateId",
                table: "Flyers",
                newName: "SourceCatalogItemId");

            migrationBuilder.RenameIndex(
                name: "IX_Flyers_FlyerTemplateId",
                table: "Flyers",
                newName: "IX_Flyers_SourceCatalogItemId");

            migrationBuilder.AddColumn<string>(
                name: "CatalogViewMode",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Flyers_CatalogItems_SourceCatalogItemId",
                table: "Flyers",
                column: "SourceCatalogItemId",
                principalTable: "CatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Flyers_CatalogItems_SourceCatalogItemId",
                table: "Flyers");

            migrationBuilder.DropColumn(
                name: "CatalogViewMode",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "SourceCatalogItemId",
                table: "Flyers",
                newName: "FlyerTemplateId");

            migrationBuilder.RenameIndex(
                name: "IX_Flyers_SourceCatalogItemId",
                table: "Flyers",
                newName: "IX_Flyers_FlyerTemplateId");

            migrationBuilder.CreateTable(
                name: "FlyerTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BackgroundCatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DefaultFontFamily = table.Column<string>(type: "text", nullable: true),
                    Fields = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_FlyerTemplates_BackgroundCatalogItemId",
                table: "FlyerTemplates",
                column: "BackgroundCatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FlyerTemplates_BandId",
                table: "FlyerTemplates",
                column: "BandId");

            migrationBuilder.AddForeignKey(
                name: "FK_Flyers_FlyerTemplates_FlyerTemplateId",
                table: "Flyers",
                column: "FlyerTemplateId",
                principalTable: "FlyerTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
