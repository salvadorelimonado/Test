using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class StoreStatusValuesInLowercase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE enrollments SET status = lower(status);
                UPDATE waitlist_entries SET status = lower(status);
                """);

            migrationBuilder.DropIndex(
                name: "ix_waitlist_entries_user_id_class_id",
                table: "waitlist_entries");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_user_id_class_id",
                table: "waitlist_entries",
                columns: new[] { "user_id", "class_id" },
                unique: true,
                filter: "status = 'waiting'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE enrollments SET status = 'Active' WHERE status = 'active';
                UPDATE enrollments SET status = 'Cancelled' WHERE status = 'cancelled';
                UPDATE waitlist_entries SET status = 'Waiting' WHERE status = 'waiting';
                UPDATE waitlist_entries SET status = 'Promoted' WHERE status = 'promoted';
                UPDATE waitlist_entries SET status = 'Cancelled' WHERE status = 'cancelled';
                UPDATE waitlist_entries SET status = 'Skipped' WHERE status = 'skipped';
                """);

            migrationBuilder.DropIndex(
                name: "ix_waitlist_entries_user_id_class_id",
                table: "waitlist_entries");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_user_id_class_id",
                table: "waitlist_entries",
                columns: new[] { "user_id", "class_id" },
                unique: true,
                filter: "status = 'Waiting'");
        }
    }
}
