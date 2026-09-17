using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace prohpharmacy_trekking_app.Migrations
{
    /// <inheritdoc />
    public partial class AddTrekNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<long>(
                name: "TrekNumberSequence",
                startValue: 1L,
                incrementBy: 1);

            migrationBuilder.Sql("""
                SELECT setval('"TrekNumberSequence"', COALESCE(
                    (SELECT MAX(CAST(SUBSTRING("TrekNumber" FROM 5) AS BIGINT)) FROM "TrekkingTrips"),
                    0
                ) + 1, false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "TrekNumberSequence");
        }
    }
}
