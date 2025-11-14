using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eSim.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusToOrderItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "OrderItems");
        }
    }
}
