using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGigActId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActId",
                table: "Gigs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Gigs_ActId",
                table: "Gigs",
                column: "ActId");

            migrationBuilder.AddForeignKey(
                name: "FK_Gigs_Acts_ActId",
                table: "Gigs",
                column: "ActId",
                principalTable: "Acts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill: every existing Gig gets its own band's default Act
            // - the same one every existing Band just got from the AddAct
            // migration's own backfill, so this always finds exactly one
            // match per Gig.
            migrationBuilder.Sql("""
                UPDATE "Gigs" g
                SET "ActId" = a."Id"
                FROM "Acts" a
                WHERE a."BandId" = g."BandId" AND a."IsDefault" = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gigs_Acts_ActId",
                table: "Gigs");

            migrationBuilder.DropIndex(
                name: "IX_Gigs_ActId",
                table: "Gigs");

            migrationBuilder.DropColumn(
                name: "ActId",
                table: "Gigs");
        }
    }
}
