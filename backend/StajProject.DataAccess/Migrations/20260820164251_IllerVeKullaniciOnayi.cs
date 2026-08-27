using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace StajProject.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class IllerVeKullaniciOnayi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_approved",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<Geometry>(
                name: "geom",
                table: "geo_permissions",
                type: "geometry(Geometry, 4326)",
                nullable: false,
                oldClrType: typeof(Polygon),
                oldType: "geometry(Polygon, 4326)");

            migrationBuilder.CreateTable(
                name: "iller",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    ad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bolge = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    geom = table.Column<Geometry>(type: "geometry(Geometry, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iller", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_iller_ad",
                table: "iller",
                column: "ad",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_iller_bolge",
                table: "iller",
                column: "bolge");

            migrationBuilder.CreateIndex(
                name: "IX_iller_geom",
                table: "iller",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "iller");

            migrationBuilder.DropColumn(
                name: "is_approved",
                table: "users");

            migrationBuilder.AlterColumn<Polygon>(
                name: "geom",
                table: "geo_permissions",
                type: "geometry(Polygon, 4326)",
                nullable: false,
                oldClrType: typeof(Geometry),
                oldType: "geometry(Geometry, 4326)");
        }
    }
}
