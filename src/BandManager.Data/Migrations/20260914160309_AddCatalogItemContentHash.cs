using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogItemContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CatalogItems_BandId",
                table: "CatalogItems");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "CatalogItems",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_BandId_ContentHash",
                table: "CatalogItems",
                columns: new[] { "BandId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CatalogItems_BandId_ContentHash",
                table: "CatalogItems");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "CatalogItems");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_BandId",
                table: "CatalogItems",
                column: "BandId");
        }
    }
}
