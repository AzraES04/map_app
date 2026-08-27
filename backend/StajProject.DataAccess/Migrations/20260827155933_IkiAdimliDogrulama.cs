using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StajProject.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class IkiAdimliDogrulama : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "totp_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "totp_secret",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "totp_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_secret",
                table: "users");
        }
    }
}
