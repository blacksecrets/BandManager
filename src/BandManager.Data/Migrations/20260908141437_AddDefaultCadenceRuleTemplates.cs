using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultCadenceRuleTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefaultCadenceRuleTemplates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    PlatformId = table.Column<string>(type: "text", nullable: false),
                    ContentTypeId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ScheduleType = table.Column<int>(type: "integer", nullable: true),
                    ScheduleDays = table.Column<string>(type: "text", nullable: true),
                    MessageTemplates = table.Column<string>(type: "text", nullable: true),
                    ManualInstructions = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefaultCadenceRuleTemplates", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DefaultCadenceRuleTemplates");
        }
    }
}
