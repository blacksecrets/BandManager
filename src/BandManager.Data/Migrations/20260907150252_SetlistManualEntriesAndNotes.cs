using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class SetlistManualEntriesAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SongId",
                table: "GigSetSongs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "ManualArtist",
                table: "GigSetSongs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManualLengthSeconds",
                table: "GigSetSongs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualSpotifyUrl",
                table: "GigSetSongs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualTitle",
                table: "GigSetSongs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualYouTubeUrl",
                table: "GigSetSongs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrintPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FontFamily = table.Column<string>(type: "text", nullable: false),
                    Bold = table.Column<bool>(type: "boolean", nullable: false),
                    Italic = table.Column<bool>(type: "boolean", nullable: false),
                    FontSizePt = table.Column<int>(type: "integer", nullable: false),
                    LineSpacing = table.Column<int>(type: "integer", nullable: false),
                    NumberLines = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SongNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepertoireEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SongNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SongNotes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SongNotes_RepertoireEntries_RepertoireEntryId",
                        column: x => x.RepertoireEntryId,
                        principalTable: "RepertoireEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintPreferences_UserId",
                table: "PrintPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SongNotes_RepertoireEntryId",
                table: "SongNotes",
                column: "RepertoireEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SongNotes_UserId_RepertoireEntryId",
                table: "SongNotes",
                columns: new[] { "UserId", "RepertoireEntryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintPreferences");

            migrationBuilder.DropTable(
                name: "SongNotes");

            migrationBuilder.DropColumn(
                name: "ManualArtist",
                table: "GigSetSongs");

            migrationBuilder.DropColumn(
                name: "ManualLengthSeconds",
                table: "GigSetSongs");

            migrationBuilder.DropColumn(
                name: "ManualSpotifyUrl",
                table: "GigSetSongs");

            migrationBuilder.DropColumn(
                name: "ManualTitle",
                table: "GigSetSongs");

            migrationBuilder.DropColumn(
                name: "ManualYouTubeUrl",
                table: "GigSetSongs");

            migrationBuilder.AlterColumn<Guid>(
                name: "SongId",
                table: "GigSetSongs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
