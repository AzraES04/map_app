using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StajProject.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class TurModulu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tour",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    scheduled_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    guide_user_id = table.Column<int>(type: "integer", nullable: true),
                    route = table.Column<LineString>(type: "geometry(LineString, 4326)", nullable: true),
                    route_distance_meters = table.Column<double>(type: "double precision", nullable: true),
                    route_duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    route_calculated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    route_signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tour", x => x.id);
                    table.ForeignKey(
                        name: "FK_tour_users_guide_user_id",
                        column: x => x.guide_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "waypoint",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tour_id = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    place_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    poi_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    venue_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dwell_minutes = table.Column<int>(type: "integer", nullable: false),
                    geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waypoint", x => x.id);
                    table.ForeignKey(
                        name: "FK_waypoint_poi_poi_id",
                        column: x => x.poi_id,
                        principalTable: "poi",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_waypoint_tour_tour_id",
                        column: x => x.tour_id,
                        principalTable: "tour",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tour_session",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tour_id = table.Column<int>(type: "integer", nullable: false),
                    guide_user_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    join_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    started_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ended_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_waypoint_id = table.Column<int>(type: "integer", nullable: true),
                    current_waypoint_arrived_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_position = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    last_position_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    progress_percent = table.Column<double>(type: "double precision", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    modified_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tour_session", x => x.id);
                    table.ForeignKey(
                        name: "FK_tour_session_tour_tour_id",
                        column: x => x.tour_id,
                        principalTable: "tour",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tour_session_users_guide_user_id",
                        column: x => x.guide_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tour_session_waypoint_current_waypoint_id",
                        column: x => x.current_waypoint_id,
                        principalTable: "waypoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tour_session_participant",
                columns: table => new
                {
                    tour_session_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    joined_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    left_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tour_session_participant", x => new { x.tour_session_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_tour_session_participant_tour_session_tour_session_id",
                        column: x => x.tour_session_id,
                        principalTable: "tour_session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tour_session_participant_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tour_guide_user_id",
                table: "tour",
                column: "guide_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tour_route",
                table: "tour",
                column: "route")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_tour_session_current_waypoint_id",
                table: "tour_session",
                column: "current_waypoint_id");

            migrationBuilder.CreateIndex(
                name: "IX_tour_session_guide_user_id",
                table: "tour_session",
                column: "guide_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tour_session_join_code",
                table: "tour_session",
                column: "join_code",
                unique: true,
                filter: "status IN ('Planned', 'Live', 'Paused') AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_tour_session_tour_id_status",
                table: "tour_session",
                columns: new[] { "tour_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_tour_session_participant_user_id",
                table: "tour_session_participant",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_waypoint_geom",
                table: "waypoint",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_waypoint_place_id",
                table: "waypoint",
                column: "place_id");

            migrationBuilder.CreateIndex(
                name: "IX_waypoint_poi_id",
                table: "waypoint",
                column: "poi_id");

            migrationBuilder.CreateIndex(
                name: "IX_waypoint_tour_id_sort_order",
                table: "waypoint",
                columns: new[] { "tour_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tour_session_participant");

            migrationBuilder.DropTable(
                name: "tour_session");

            migrationBuilder.DropTable(
                name: "waypoint");

            migrationBuilder.DropTable(
                name: "tour");
        }
    }
}
