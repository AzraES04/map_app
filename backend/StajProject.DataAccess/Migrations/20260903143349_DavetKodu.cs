using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StajProject.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DavetKodu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invite_code",
                table: "users",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "parent_admin_id",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_invite_code",
                table: "users",
                column: "invite_code",
                unique: true,
                filter: "is_deleted = false AND invite_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_parent_admin_id",
                table: "users",
                column: "parent_admin_id");

            migrationBuilder.AddForeignKey(
                name: "FK_users_users_parent_admin_id",
                table: "users",
                column: "parent_admin_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_users_parent_admin_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_invite_code",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_parent_admin_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "invite_code",
                table: "users");

            migrationBuilder.DropColumn(
                name: "parent_admin_id",
                table: "users");
        }
    }
}
