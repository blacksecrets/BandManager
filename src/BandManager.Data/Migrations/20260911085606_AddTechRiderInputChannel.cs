using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTechRiderInputChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TechRiderInputChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelNumber = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    MicRecommendation = table.Column<string>(type: "text", nullable: true),
                    ProvidedBy = table.Column<string>(type: "text", nullable: true),
                    PositioningNotes = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechRiderInputChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TechRiderInputChannels_Acts_ActId",
                        column: x => x.ActId,
                        principalTable: "Acts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TechRiderInputChannels_ActId",
                table: "TechRiderInputChannels",
                column: "ActId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TechRiderInputChannels");
        }
    }
}
