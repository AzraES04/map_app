using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace StajProject.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class GuzergahRotasi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<LineString>(
                name: "rota",
                table: "guzergah",
                type: "geometry(LineString, 4326)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rota_hesaplandi",
                table: "guzergah",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rota_imza",
                table: "guzergah",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "rota_mesafe_metre",
                table: "guzergah",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "rota_sure_saniye",
                table: "guzergah",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_guzergah_rota",
                table: "guzergah",
                column: "rota")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_guzergah_rota",
                table: "guzergah");

            migrationBuilder.DropColumn(
                name: "rota",
                table: "guzergah");

            migrationBuilder.DropColumn(
                name: "rota_hesaplandi",
                table: "guzergah");

            migrationBuilder.DropColumn(
                name: "rota_imza",
                table: "guzergah");

            migrationBuilder.DropColumn(
                name: "rota_mesafe_metre",
                table: "guzergah");

            migrationBuilder.DropColumn(
                name: "rota_sure_saniye",
                table: "guzergah");
        }
    }
}
