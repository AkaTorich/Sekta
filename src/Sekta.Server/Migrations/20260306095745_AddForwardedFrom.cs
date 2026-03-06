using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sekta.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddForwardedFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ForwardedFrom",
                table: "Messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForwardedFrom",
                table: "Messages");
        }
    }
}
