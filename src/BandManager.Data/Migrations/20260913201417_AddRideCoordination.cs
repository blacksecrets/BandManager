using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRideCoordination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GigMeetingPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    GigRef = table.Column<string>(type: "text", nullable: false),
                    MeetingPoint = table.Column<string>(type: "text", nullable: true),
                    MeetingTime = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigMeetingPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigMeetingPoints_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GigMeetingPoints_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GigRideOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    GigRef = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatsAvailable = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigRideOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigRideOffers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GigRideOffers_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GigMeetingPoints_BandId_GigRef",
                table: "GigMeetingPoints",
                columns: new[] { "BandId", "GigRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GigMeetingPoints_UpdatedByUserId",
                table: "GigMeetingPoints",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GigRideOffers_BandId_GigRef_UserId",
                table: "GigRideOffers",
                columns: new[] { "BandId", "GigRef", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GigRideOffers_UserId",
                table: "GigRideOffers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GigMeetingPoints");

            migrationBuilder.DropTable(
                name: "GigRideOffers");
        }
    }
}
