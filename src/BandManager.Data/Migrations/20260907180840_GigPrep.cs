using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class GigPrep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GigPrepChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GigId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListType = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    IsChecked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigPrepChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigPrepChecklistItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GigPrepChecklistItems_Gigs_GigId",
                        column: x => x.GigId,
                        principalTable: "Gigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GigPrepDefaultItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListType = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigPrepDefaultItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigPrepDefaultItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GigPrepChecklistItems_GigId",
                table: "GigPrepChecklistItems",
                column: "GigId");

            migrationBuilder.CreateIndex(
                name: "IX_GigPrepChecklistItems_UserId",
                table: "GigPrepChecklistItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GigPrepDefaultItems_UserId",
                table: "GigPrepDefaultItems",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GigPrepChecklistItems");

            migrationBuilder.DropTable(
                name: "GigPrepDefaultItems");
        }
    }
}
