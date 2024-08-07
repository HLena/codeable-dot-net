using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CachedInventory.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEventSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Events");

            migrationBuilder.AddColumn<bool>(
                name: "IsRestock",
                table: "Events",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRestock",
                table: "Events");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Events",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
