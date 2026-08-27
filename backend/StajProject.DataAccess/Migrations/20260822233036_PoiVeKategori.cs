using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StajProject.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class PoiVeKategori : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "poi_category",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ad = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    aciklama = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poi_category", x => x.id);
                    table.ForeignKey(
                        name: "FK_poi_category_poi_category_parent_id",
                        column: x => x.parent_id,
                        principalTable: "poi_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "poi",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    isim = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kategori_id = table.Column<int>(type: "integer", nullable: false),
                    mesai_saatleri = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poi", x => x.id);
                    table.ForeignKey(
                        name: "FK_poi_poi_category_kategori_id",
                        column: x => x.kategori_id,
                        principalTable: "poi_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_poi_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_poi_geom",
                table: "poi",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_poi_kategori_id",
                table: "poi",
                column: "kategori_id");

            migrationBuilder.CreateIndex(
                name: "IX_poi_user_id",
                table: "poi",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_poi_category_parent_id",
                table: "poi_category",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_poi_category_parent_id_ad",
                table: "poi_category",
                columns: new[] { "parent_id", "ad" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "poi");

            migrationBuilder.DropTable(
                name: "poi_category");
        }
    }
}
