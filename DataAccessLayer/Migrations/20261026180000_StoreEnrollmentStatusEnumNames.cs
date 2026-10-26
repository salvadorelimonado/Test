using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class StoreEnrollmentStatusEnumNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE enrollments SET status = 'Active' WHERE status = 'active';
                UPDATE enrollments SET status = 'Cancelled' WHERE status = 'cancelled';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE enrollments SET status = 'active' WHERE status = 'Active';
                UPDATE enrollments SET status = 'cancelled' WHERE status = 'Cancelled';
                """);
        }
    }
}
