using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AudienceCapacity",
                table: "Venues",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultPromoterId",
                table: "Venues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StageDepthFeet",
                table: "Venues",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StageWidthFeet",
                table: "Venues",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromoterId",
                table: "Gigs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Promoters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Company = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promoters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promoters_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Venues_DefaultPromoterId",
                table: "Venues",
                column: "DefaultPromoterId");

            migrationBuilder.CreateIndex(
                name: "IX_Gigs_PromoterId",
                table: "Gigs",
                column: "PromoterId");

            migrationBuilder.CreateIndex(
                name: "IX_Promoters_BandId",
                table: "Promoters",
                column: "BandId");

            migrationBuilder.AddForeignKey(
                name: "FK_Gigs_Promoters_PromoterId",
                table: "Gigs",
                column: "PromoterId",
                principalTable: "Promoters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Venues_Promoters_DefaultPromoterId",
                table: "Venues",
                column: "DefaultPromoterId",
                principalTable: "Promoters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gigs_Promoters_PromoterId",
                table: "Gigs");

            migrationBuilder.DropForeignKey(
                name: "FK_Venues_Promoters_DefaultPromoterId",
                table: "Venues");

            migrationBuilder.DropTable(
                name: "Promoters");

            migrationBuilder.DropIndex(
                name: "IX_Venues_DefaultPromoterId",
                table: "Venues");

            migrationBuilder.DropIndex(
                name: "IX_Gigs_PromoterId",
                table: "Gigs");

            migrationBuilder.DropColumn(
                name: "AudienceCapacity",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "DefaultPromoterId",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "StageDepthFeet",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "StageWidthFeet",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "PromoterId",
                table: "Gigs");
        }
    }
}
