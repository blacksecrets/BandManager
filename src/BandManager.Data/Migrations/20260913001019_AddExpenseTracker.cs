using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BandLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AddressLine1 = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    PostalCode = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BandLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BandLocations_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    FromIsHome = table.Column<bool>(type: "boolean", nullable: false),
                    FromName = table.Column<string>(type: "text", nullable: true),
                    FromAddressLine1 = table.Column<string>(type: "text", nullable: true),
                    FromCity = table.Column<string>(type: "text", nullable: true),
                    FromState = table.Column<string>(type: "text", nullable: true),
                    FromPostalCode = table.Column<string>(type: "text", nullable: true),
                    ToIsHome = table.Column<bool>(type: "boolean", nullable: false),
                    ToName = table.Column<string>(type: "text", nullable: true),
                    ToAddressLine1 = table.Column<string>(type: "text", nullable: true),
                    ToCity = table.Column<string>(type: "text", nullable: true),
                    ToState = table.Column<string>(type: "text", nullable: true),
                    ToPostalCode = table.Column<string>(type: "text", nullable: true),
                    DistanceMiles = table.Column<double>(type: "double precision", nullable: true),
                    RoundTrip = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    OtherReasonText = table.Column<string>(type: "text", nullable: true),
                    GigRef = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trips_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTravelProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cadence = table.Column<int>(type: "integer", nullable: false),
                    VehicleMake = table.Column<string>(type: "text", nullable: true),
                    VehicleModel = table.Column<string>(type: "text", nullable: true),
                    VehicleYear = table.Column<int>(type: "integer", nullable: true),
                    StartingMileage = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTravelProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTravelProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BandLocations_BandId_Name",
                table: "BandLocations",
                columns: new[] { "BandId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_BandId",
                table: "Trips",
                column: "BandId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_UserId_BandId_Date",
                table: "Trips",
                columns: new[] { "UserId", "BandId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTravelProfiles_UserId",
                table: "UserTravelProfiles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BandLocations");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropTable(
                name: "UserTravelProfiles");
        }
    }
}
