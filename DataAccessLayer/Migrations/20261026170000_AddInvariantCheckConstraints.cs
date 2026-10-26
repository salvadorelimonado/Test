using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddInvariantCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_passes_remaining_visits",
                table: "passes",
                sql: "remaining_visits >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_enrollments_seats",
                table: "enrollments",
                sql: "seats > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_waitlist_entries_seats",
                table: "waitlist_entries",
                sql: "seats > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_passes_remaining_visits",
                table: "passes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_enrollments_seats",
                table: "enrollments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_waitlist_entries_seats",
                table: "waitlist_entries");
        }
    }
}
