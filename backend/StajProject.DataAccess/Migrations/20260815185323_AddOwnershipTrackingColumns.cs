using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StajProject.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnershipTrackingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "tbl_polygon",
                newName: "inserted_date");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "tbl_point",
                newName: "inserted_date");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "tbl_line",
                newName: "inserted_date");

            migrationBuilder.AddColumn<int>(
                name: "inserted_user_id",
                table: "tbl_polygon",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "inserted_user_id",
                table: "tbl_point",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "inserted_user_id",
                table: "tbl_line",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tbl_polygon_inserted_user_id",
                table: "tbl_polygon",
                column: "inserted_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tbl_point_inserted_user_id",
                table: "tbl_point",
                column: "inserted_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_tbl_line_inserted_user_id",
                table: "tbl_line",
                column: "inserted_user_id");

            // ---- VERİ DEVRİ (Ödev 5) ----
            // Bu kolon eklenmeden önce oluşturulmuş kayıtların sahibi yok.
            // Harita artık yalnızca giriş yapan kullanıcının çizimlerini
            // listelediği için, sahipsiz kayıtlar ekrandan kaybolurdu.
            // Onları ilk kullanıcıya (demo admin) devrediyoruz.
            //
            // Şema değişikliğiyle birlikte veriyi de taşımak migration'ın işidir:
            // aynı migration'ı çalıştıran herkes aynı sonuca ulaşır.
            migrationBuilder.Sql(@"
                UPDATE tbl_point   SET inserted_user_id = (SELECT MIN(id) FROM users)
                WHERE inserted_user_id IS NULL;

                UPDATE tbl_line    SET inserted_user_id = (SELECT MIN(id) FROM users)
                WHERE inserted_user_id IS NULL;

                UPDATE tbl_polygon SET inserted_user_id = (SELECT MIN(id) FROM users)
                WHERE inserted_user_id IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tbl_polygon_inserted_user_id",
                table: "tbl_polygon");

            migrationBuilder.DropIndex(
                name: "IX_tbl_point_inserted_user_id",
                table: "tbl_point");

            migrationBuilder.DropIndex(
                name: "IX_tbl_line_inserted_user_id",
                table: "tbl_line");

            migrationBuilder.DropColumn(
                name: "inserted_user_id",
                table: "tbl_polygon");

            migrationBuilder.DropColumn(
                name: "inserted_user_id",
                table: "tbl_point");

            migrationBuilder.DropColumn(
                name: "inserted_user_id",
                table: "tbl_line");

            migrationBuilder.RenameColumn(
                name: "inserted_date",
                table: "tbl_polygon",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "inserted_date",
                table: "tbl_point",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "inserted_date",
                table: "tbl_line",
                newName: "created_at");
        }
    }
}
