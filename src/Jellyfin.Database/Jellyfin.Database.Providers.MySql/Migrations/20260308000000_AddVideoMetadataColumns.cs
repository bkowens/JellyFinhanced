using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Providers.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoMetadataColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeriesStatus",
                table: "BaseItems",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoType",
                table: "BaseItems",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Is3D",
                table: "BaseItems",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPlaceHolder",
                table: "BaseItems",
                type: "tinyint(1)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SeriesStatus", table: "BaseItems");
            migrationBuilder.DropColumn(name: "VideoType", table: "BaseItems");
            migrationBuilder.DropColumn(name: "Is3D", table: "BaseItems");
            migrationBuilder.DropColumn(name: "IsPlaceHolder", table: "BaseItems");
        }
    }
}
