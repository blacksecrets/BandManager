using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRepertoireEntryVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepertoireEntryVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepertoireEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: true),
                    ClickTrackConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepertoireEntryVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepertoireEntryVideos_CatalogItems_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RepertoireEntryVideos_RepertoireEntries_RepertoireEntryId",
                        column: x => x.RepertoireEntryId,
                        principalTable: "RepertoireEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepertoireEntryVideoBreakpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepertoireEntryVideoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    TimestampSeconds = table.Column<double>(type: "double precision", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsSongStart = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepertoireEntryVideoBreakpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepertoireEntryVideoBreakpoints_RepertoireEntryVideos_Reper~",
                        column: x => x.RepertoireEntryVideoId,
                        principalTable: "RepertoireEntryVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepertoireEntryVideoTempoSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepertoireEntryVideoId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTimestampSeconds = table.Column<double>(type: "double precision", nullable: false),
                    Bpm = table.Column<decimal>(type: "numeric", nullable: false),
                    BeatOffsetSeconds = table.Column<decimal>(type: "numeric", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepertoireEntryVideoTempoSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepertoireEntryVideoTempoSegments_RepertoireEntryVideos_Rep~",
                        column: x => x.RepertoireEntryVideoId,
                        principalTable: "RepertoireEntryVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireEntryVideoBreakpoints_RepertoireEntryVideoId",
                table: "RepertoireEntryVideoBreakpoints",
                column: "RepertoireEntryVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireEntryVideos_CatalogItemId",
                table: "RepertoireEntryVideos",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireEntryVideos_RepertoireEntryId",
                table: "RepertoireEntryVideos",
                column: "RepertoireEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepertoireEntryVideoTempoSegments_RepertoireEntryVideoId",
                table: "RepertoireEntryVideoTempoSegments",
                column: "RepertoireEntryVideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepertoireEntryVideoBreakpoints");

            migrationBuilder.DropTable(
                name: "RepertoireEntryVideoTempoSegments");

            migrationBuilder.DropTable(
                name: "RepertoireEntryVideos");
        }
    }
}
