using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadalBooking.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformerApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role_valid",
                schema: "app",
                table: "users");

            migrationBuilder.CreateTable(
                name: "performer_applications",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_performer_applications", x => x.id);
                    table.CheckConstraint("ck_performer_applications_decision_fields", "(status = 'Pending') = (decided_at_utc IS NULL AND decided_by_user_id IS NULL)");
                    table.CheckConstraint("ck_performer_applications_status_valid", "status IN ('Pending', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_performer_applications_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_performer_applications_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role_valid",
                schema: "app",
                table: "users",
                sql: "role IN ('Customer', 'Performer', 'Admin')");

            migrationBuilder.CreateIndex(
                name: "ix_performer_applications_decided_by_user_id",
                schema: "app",
                table: "performer_applications",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_performer_applications_status_created_at_utc",
                schema: "app",
                table: "performer_applications",
                columns: new[] { "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_performer_applications_one_pending_per_user",
                schema: "app",
                table: "performer_applications",
                column: "user_id",
                unique: true,
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "performer_applications",
                schema: "app");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_role_valid",
                schema: "app",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_role_valid",
                schema: "app",
                table: "users",
                sql: "role IN ('Customer', 'Performer')");
        }
    }
}
