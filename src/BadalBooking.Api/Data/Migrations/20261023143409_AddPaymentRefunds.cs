using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadalBooking.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_status_valid",
                schema: "app",
                table: "payments");

            migrationBuilder.AddColumn<string>(
                name: "refund_reference",
                schema: "app",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refunded_at_utc",
                schema: "app",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_refund_fields",
                schema: "app",
                table: "payments",
                sql: "(status = 'Refunded') = (refunded_at_utc IS NOT NULL AND refund_reference IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_status_valid",
                schema: "app",
                table: "payments",
                sql: "status IN ('Succeeded', 'Failed', 'Refunded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_refund_fields",
                schema: "app",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_status_valid",
                schema: "app",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_reference",
                schema: "app",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refunded_at_utc",
                schema: "app",
                table: "payments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_status_valid",
                schema: "app",
                table: "payments",
                sql: "status IN ('Succeeded', 'Failed')");
        }
    }
}
