using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using prohpharmacy_trekking_app.Database;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113153000_AddDocumentMatchJobs")]
public sealed class AddDocumentMatchJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DocumentMatchJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                TotalFiles = table.Column<int>(type: "integer", nullable: false),
                ProcessedFiles = table.Column<int>(type: "integer", nullable: false),
                CurrentFileIndex = table.Column<int>(type: "integer", nullable: true),
                CurrentPage = table.Column<int>(type: "integer", nullable: true),
                CurrentFilePages = table.Column<int>(type: "integer", nullable: true),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                InputManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                ExcelMappingsJson = table.Column<string>(type: "jsonb", nullable: false),
                ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DocumentMatchJobs", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DocumentMatchJobs_ExpiresAt",
            table: "DocumentMatchJobs",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_DocumentMatchJobs_Status_CreatedAt",
            table: "DocumentMatchJobs",
            columns: new[] { "Status", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "DocumentMatchJobs");
}
