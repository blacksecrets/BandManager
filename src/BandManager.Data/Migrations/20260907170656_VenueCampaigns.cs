using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class VenueCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VenueId",
                table: "Gigs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VenueCadenceSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepNumber = table.Column<int>(type: "integer", nullable: false),
                    DaysAfterPrevious = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    DefaultSubject = table.Column<string>(type: "text", nullable: true),
                    DefaultBody = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueCadenceSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VenueCadenceSteps_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AddressLine1 = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    PostalCode = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Venues_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VenueCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentStepNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCommunicationAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    RetryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NeverRetry = table.Column<bool>(type: "boolean", nullable: false),
                    BookedGigId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VenueCampaigns_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VenueCampaigns_Gigs_BookedGigId",
                        column: x => x.BookedGigId,
                        principalTable: "Gigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VenueCampaigns_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VenueContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VenueContacts_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VenueCommunications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueCampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    StepNumber = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    To = table.Column<string>(type: "text", nullable: true),
                    Cc = table.Column<string>(type: "text", nullable: true),
                    Bcc = table.Column<string>(type: "text", nullable: true),
                    From = table.Column<string>(type: "text", nullable: true),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    OutcomeNotes = table.Column<string>(type: "text", nullable: true),
                    LoggedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueCommunications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VenueCommunications_AspNetUsers_LoggedByUserId",
                        column: x => x.LoggedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VenueCommunications_VenueCampaigns_VenueCampaignId",
                        column: x => x.VenueCampaignId,
                        principalTable: "VenueCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Gigs_VenueId",
                table: "Gigs",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueCadenceSteps_BandId_StepNumber",
                table: "VenueCadenceSteps",
                columns: new[] { "BandId", "StepNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VenueCampaigns_BandId",
                table: "VenueCampaigns",
                column: "BandId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueCampaigns_BookedGigId",
                table: "VenueCampaigns",
                column: "BookedGigId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueCampaigns_VenueId",
                table: "VenueCampaigns",
                column: "VenueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VenueCommunications_LoggedByUserId",
                table: "VenueCommunications",
                column: "LoggedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueCommunications_VenueCampaignId",
                table: "VenueCommunications",
                column: "VenueCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueContacts_VenueId",
                table: "VenueContacts",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_Venues_BandId",
                table: "Venues",
                column: "BandId");

            migrationBuilder.AddForeignKey(
                name: "FK_Gigs_Venues_VenueId",
                table: "Gigs",
                column: "VenueId",
                principalTable: "Venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gigs_Venues_VenueId",
                table: "Gigs");

            migrationBuilder.DropTable(
                name: "VenueCadenceSteps");

            migrationBuilder.DropTable(
                name: "VenueCommunications");

            migrationBuilder.DropTable(
                name: "VenueContacts");

            migrationBuilder.DropTable(
                name: "VenueCampaigns");

            migrationBuilder.DropTable(
                name: "Venues");

            migrationBuilder.DropIndex(
                name: "IX_Gigs_VenueId",
                table: "Gigs");

            migrationBuilder.DropColumn(
                name: "VenueId",
                table: "Gigs");
        }
    }
}
