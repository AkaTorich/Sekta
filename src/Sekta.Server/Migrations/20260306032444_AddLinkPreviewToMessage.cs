using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sekta.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkPreviewToMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LinkPreviewDescription",
                table: "Messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkPreviewDomain",
                table: "Messages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkPreviewImageUrl",
                table: "Messages",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkPreviewTitle",
                table: "Messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkPreviewUrl",
                table: "Messages",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinkPreviewDescription",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "LinkPreviewDomain",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "LinkPreviewImageUrl",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "LinkPreviewTitle",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "LinkPreviewUrl",
                table: "Messages");
        }
    }
}
