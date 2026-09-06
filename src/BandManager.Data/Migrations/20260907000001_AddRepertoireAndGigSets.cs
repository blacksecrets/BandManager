using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRepertoireAndGigSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Songs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    OriginalArtist = table.Column<string>(type: "text", nullable: true),
                    Album = table.Column<string>(type: "text", nullable: true),
                    Key = table.Column<string>(type: "text", nullable: true),
                    LengthSeconds = table.Column<int>(type: "integer", nullable: true),
                    YouTubeUrl = table.Column<string>(type: "text", nullable: true),
                    SpotifyUrl = table.Column<string>(type: "text", nullable: true),
                    SongsterrUrl = table.Column<string>(type: "text", nullable: true),
                    Tunings = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Songs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BandInstruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BandInstruments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BandInstruments_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepertoireEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    SongId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepertoireEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepertoireEntries_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepertoireEntries_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GigSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    GigRef = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigSets_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GigSetSongs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GigSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SongId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigSetSongs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigSetSongs_GigSets_GigSetId",
                        column: x => x.GigSetId,
                        principalTable: "GigSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GigSetSongs_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BandInstruments_BandId",
                table: "BandInstruments",
                column: "BandId");

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireEntries_BandId_SongId",
                table: "RepertoireEntries",
                columns: new[] { "BandId", "SongId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireEntries_SongId",
                table: "RepertoireEntries",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_GigSets_BandId_GigRef",
                table: "GigSets",
                columns: new[] { "BandId", "GigRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GigSetSongs_GigSetId",
                table: "GigSetSongs",
                column: "GigSetId");

            migrationBuilder.CreateIndex(
                name: "IX_GigSetSongs_SongId",
                table: "GigSetSongs",
                column: "SongId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GigSetSongs");
            migrationBuilder.DropTable(name: "RepertoireEntries");
            migrationBuilder.DropTable(name: "GigSets");
            migrationBuilder.DropTable(name: "BandInstruments");
            migrationBuilder.DropTable(name: "Songs");
        }
    }
}
