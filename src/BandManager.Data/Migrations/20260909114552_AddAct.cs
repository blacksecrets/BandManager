using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Acts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IntroText = table.Column<string>(type: "text", nullable: true),
                    VideoNotes = table.Column<string>(type: "text", nullable: true),
                    GeneralNotes = table.Column<string>(type: "text", nullable: true),
                    TechContactName = table.Column<string>(type: "text", nullable: true),
                    TechContactPhone = table.Column<string>(type: "text", nullable: true),
                    TechContactEmail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Acts_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Acts_BandId",
                table: "Acts",
                column: "BandId");

            // Backfill: every existing Band (real tenants and with-band
            // stubs alike - harmless either way, a stub's Act is simply
            // never used) gets one default Act named after itself, same
            // as SuperAdminController.CreateBand now does for a brand-new
            // Band. gen_random_uuid() is a Postgres core builtin since
            // v13, no extension needed (this app runs on postgres:17).
            migrationBuilder.Sql("""
                INSERT INTO "Acts" ("Id", "BandId", "Name", "IsDefault", "CreatedAt")
                SELECT gen_random_uuid(), "Id", "Name", true, now() FROM "Bands";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Acts");
        }
    }
}
