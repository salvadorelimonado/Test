using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class IncludeSeatsInEnrollmentClassStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_enrollments_class_id_status",
                table: "enrollments");

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_class_id_status",
                table: "enrollments",
                columns: new[] { "class_id", "status" })
                .Annotation("Npgsql:IndexInclude", new[] { "seats" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_enrollments_class_id_status",
                table: "enrollments");

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_class_id_status",
                table: "enrollments",
                columns: new[] { "class_id", "status" });
        }
    }
}
