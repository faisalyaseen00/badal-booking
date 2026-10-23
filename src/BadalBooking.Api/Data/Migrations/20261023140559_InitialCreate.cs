using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadalBooking.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_role_valid", "role IN ('Customer', 'Performer')");
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pilgrim_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount_halalas = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    payment_deadline_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_accepted_has_performer", "(status = 'Accepted') = (performer_id IS NOT NULL)");
                    table.CheckConstraint("ck_bookings_amount_positive", "amount_halalas > 0");
                    table.CheckConstraint("ck_bookings_currency_sar", "currency = 'SAR'");
                    table.CheckConstraint("ck_bookings_status_valid", "status IN ('PendingPayment', 'Paid', 'Accepted', 'Cancelled', 'Expired')");
                    table.ForeignKey(
                        name: "fk_bookings_users_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_users_performer_id",
                        column: x => x.performer_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_id_created_at_utc",
                schema: "app",
                table: "bookings",
                columns: new[] { "customer_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_performer_id",
                schema: "app",
                table: "bookings",
                column: "performer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_payment_deadline_utc",
                schema: "app",
                table: "bookings",
                columns: new[] { "status", "payment_deadline_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "app",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings",
                schema: "app");

            migrationBuilder.DropTable(
                name: "users",
                schema: "app");
        }
    }
}
