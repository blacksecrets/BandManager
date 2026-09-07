using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteContentAsDbRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default true, not EF's usual bool-column false: every already-
            // real Band (Black Secrets, Attica, ...) must stay onboarded
            // across this migration - only a with-band stub created after
            // this ships (GigsController.ResolveWithActsAsync) explicitly
            // sets false.
            migrationBuilder.AddColumn<bool>(
                name: "IsOnboarded",
                table: "Bands",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "GalleryImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ref = table.Column<string>(type: "text", nullable: false),
                    Alt = table.Column<string>(type: "text", nullable: true),
                    Thumb = table.Column<string>(type: "text", nullable: true),
                    Full = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GalleryImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GalleryImages_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Gigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ref = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Venue = table.Column<string>(type: "text", nullable: true),
                    VenueUrl = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    DoorsTime = table.Column<string>(type: "text", nullable: true),
                    OpenerTime = table.Column<string>(type: "text", nullable: true),
                    HeadlinerTime = table.Column<string>(type: "text", nullable: true),
                    TicketsUrl = table.Column<string>(type: "text", nullable: true),
                    FlyerMain = table.Column<string>(type: "text", nullable: true),
                    FreeAdmission = table.Column<bool>(type: "boolean", nullable: false),
                    CustomTicketsText = table.Column<string>(type: "text", nullable: true),
                    TicketMode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Gigs_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ref = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true),
                    Thumbnail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItems_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GigWithBands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GigId = table.Column<Guid>(type: "uuid", nullable: false),
                    WithBandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigWithBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigWithBands_Bands_WithBandId",
                        column: x => x.WithBandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GigWithBands_Gigs_GigId",
                        column: x => x.GigId,
                        principalTable: "Gigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GalleryImages_BandId_Ref",
                table: "GalleryImages",
                columns: new[] { "BandId", "Ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Gigs_BandId_Ref",
                table: "Gigs",
                columns: new[] { "BandId", "Ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GigWithBands_GigId",
                table: "GigWithBands",
                column: "GigId");

            migrationBuilder.CreateIndex(
                name: "IX_GigWithBands_WithBandId",
                table: "GigWithBands",
                column: "WithBandId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_BandId_Ref",
                table: "MediaItems",
                columns: new[] { "BandId", "Ref" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GalleryImages");

            migrationBuilder.DropTable(
                name: "GigWithBands");

            migrationBuilder.DropTable(
                name: "MediaItems");

            migrationBuilder.DropTable(
                name: "Gigs");

            migrationBuilder.DropColumn(
                name: "IsOnboarded",
                table: "Bands");
        }
    }
}
