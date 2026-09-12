using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultGigPayeeUserId",
                table: "Bands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultMerchPayeeUserId",
                table: "Bands",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GigPayoutRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GigId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    PayoutType = table.Column<int>(type: "integer", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigPayoutRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigPayoutRecipients_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GigPayoutRecipients_Gigs_GigId",
                        column: x => x.GigId,
                        principalTable: "Gigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GigPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GigId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    PaidByFirstName = table.Column<string>(type: "text", nullable: true),
                    PaidByLastName = table.Column<string>(type: "text", nullable: true),
                    PaidByOrganization = table.Column<string>(type: "text", nullable: true),
                    PaidByEmail = table.Column<string>(type: "text", nullable: true),
                    PaidByPhone = table.Column<string>(type: "text", nullable: true),
                    PayoutType = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GigPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GigPayouts_Gigs_GigId",
                        column: x => x.GigId,
                        principalTable: "Gigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayoutRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutRecipients_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayoutRecipients_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bands_DefaultGigPayeeUserId",
                table: "Bands",
                column: "DefaultGigPayeeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bands_DefaultMerchPayeeUserId",
                table: "Bands",
                column: "DefaultMerchPayeeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GigPayoutRecipients_GigId_UserId",
                table: "GigPayoutRecipients",
                columns: new[] { "GigId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GigPayoutRecipients_UserId",
                table: "GigPayoutRecipients",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GigPayouts_GigId",
                table: "GigPayouts",
                column: "GigId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRecipients_BandId_UserId",
                table: "PayoutRecipients",
                columns: new[] { "BandId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRecipients_UserId",
                table: "PayoutRecipients",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bands_AspNetUsers_DefaultGigPayeeUserId",
                table: "Bands",
                column: "DefaultGigPayeeUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Bands_AspNetUsers_DefaultMerchPayeeUserId",
                table: "Bands",
                column: "DefaultMerchPayeeUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bands_AspNetUsers_DefaultGigPayeeUserId",
                table: "Bands");

            migrationBuilder.DropForeignKey(
                name: "FK_Bands_AspNetUsers_DefaultMerchPayeeUserId",
                table: "Bands");

            migrationBuilder.DropTable(
                name: "GigPayoutRecipients");

            migrationBuilder.DropTable(
                name: "GigPayouts");

            migrationBuilder.DropTable(
                name: "PayoutRecipients");

            migrationBuilder.DropIndex(
                name: "IX_Bands_DefaultGigPayeeUserId",
                table: "Bands");

            migrationBuilder.DropIndex(
                name: "IX_Bands_DefaultMerchPayeeUserId",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "DefaultGigPayeeUserId",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "DefaultMerchPayeeUserId",
                table: "Bands");
        }
    }
}
