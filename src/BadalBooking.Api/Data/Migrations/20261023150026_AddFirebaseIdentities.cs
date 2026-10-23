using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadalBooking.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFirebaseIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "firebase_identities",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firebase_uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firebase_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_firebase_identities_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_firebase_identities_firebase_uid",
                schema: "app",
                table: "firebase_identities",
                column: "firebase_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_firebase_identities_user_id",
                schema: "app",
                table: "firebase_identities",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "firebase_identities",
                schema: "app");
        }
    }
}
