using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistPromotedEnrollmentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "promoted_enrollment_id",
                table: "waitlist_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_promoted_enrollment_id",
                table: "waitlist_entries",
                column: "promoted_enrollment_id");

            migrationBuilder.AddForeignKey(
                name: "fk_waitlist_entries_enrollments_promoted_enrollment_id",
                table: "waitlist_entries",
                column: "promoted_enrollment_id",
                principalTable: "enrollments",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_waitlist_entries_enrollments_promoted_enrollment_id",
                table: "waitlist_entries");

            migrationBuilder.DropIndex(
                name: "ix_waitlist_entries_promoted_enrollment_id",
                table: "waitlist_entries");

            migrationBuilder.DropColumn(
                name: "promoted_enrollment_id",
                table: "waitlist_entries");
        }
    }
}
