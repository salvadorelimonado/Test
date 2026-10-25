using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyRecordAndWaitlistEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_classes_rooms_room_id",
                table: "classes");

            migrationBuilder.DropForeignKey(
                name: "FK_enrollments_classes_class_id",
                table: "enrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_enrollments_users_user_id",
                table: "enrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_passes_users_user_id",
                table: "passes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_rooms",
                table: "rooms");

            migrationBuilder.DropPrimaryKey(
                name: "PK_passes",
                table: "passes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_enrollments",
                table: "enrollments");

            migrationBuilder.DropIndex(
                name: "IX_enrollments_class_id",
                table: "enrollments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_classes",
                table: "classes");

            migrationBuilder.RenameIndex(
                name: "IX_passes_user_id",
                table: "passes",
                newName: "ix_passes_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_enrollments_user_id",
                table: "enrollments",
                newName: "ix_enrollments_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_classes_room_id",
                table: "classes",
                newName: "ix_classes_room_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_users",
                table: "users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_rooms",
                table: "rooms",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_passes",
                table: "passes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_enrollments",
                table: "enrollments",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_classes",
                table: "classes",
                column: "id");

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "text", nullable: false),
                    key_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    request_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    response_body = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_idempotency_records_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    class_id = table.Column<long>(type: "bigint", nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waitlist_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_class_id_status",
                table: "enrollments",
                columns: new[] { "class_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_classes_starts_at_id",
                table: "classes",
                columns: new[] { "starts_at", "id" },
                filter: "is_cancelled = false");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_user_id_operation_key_hash",
                table: "idempotency_records",
                columns: new[] { "user_id", "operation", "key_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_class_id_status_created_at_id",
                table: "waitlist_entries",
                columns: new[] { "class_id", "status", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_user_id_class_id",
                table: "waitlist_entries",
                columns: new[] { "user_id", "class_id" },
                unique: true,
                filter: "status = 'Waiting'");

            migrationBuilder.AddForeignKey(
                name: "fk_classes_rooms_room_id",
                table: "classes",
                column: "room_id",
                principalTable: "rooms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_enrollments_classes_class_id",
                table: "enrollments",
                column: "class_id",
                principalTable: "classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_enrollments_users_user_id",
                table: "enrollments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_passes_users_user_id",
                table: "passes",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_classes_rooms_room_id",
                table: "classes");

            migrationBuilder.DropForeignKey(
                name: "fk_enrollments_classes_class_id",
                table: "enrollments");

            migrationBuilder.DropForeignKey(
                name: "fk_enrollments_users_user_id",
                table: "enrollments");

            migrationBuilder.DropForeignKey(
                name: "fk_passes_users_user_id",
                table: "passes");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "waitlist_entries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_users",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_rooms",
                table: "rooms");

            migrationBuilder.DropPrimaryKey(
                name: "pk_passes",
                table: "passes");

            migrationBuilder.DropPrimaryKey(
                name: "pk_enrollments",
                table: "enrollments");

            migrationBuilder.DropIndex(
                name: "ix_enrollments_class_id_status",
                table: "enrollments");

            migrationBuilder.DropPrimaryKey(
                name: "pk_classes",
                table: "classes");

            migrationBuilder.DropIndex(
                name: "ix_classes_starts_at_id",
                table: "classes");

            migrationBuilder.RenameIndex(
                name: "ix_passes_user_id",
                table: "passes",
                newName: "IX_passes_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_enrollments_user_id",
                table: "enrollments",
                newName: "IX_enrollments_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_classes_room_id",
                table: "classes",
                newName: "IX_classes_room_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                table: "users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_rooms",
                table: "rooms",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_passes",
                table: "passes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_enrollments",
                table: "enrollments",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_classes",
                table: "classes",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_class_id",
                table: "enrollments",
                column: "class_id");

            migrationBuilder.AddForeignKey(
                name: "FK_classes_rooms_room_id",
                table: "classes",
                column: "room_id",
                principalTable: "rooms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_enrollments_classes_class_id",
                table: "enrollments",
                column: "class_id",
                principalTable: "classes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_enrollments_users_user_id",
                table: "enrollments",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_passes_users_user_id",
                table: "passes",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
