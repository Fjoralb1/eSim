using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eSim.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrderTableChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "Orders",
                newName: "RPrice");

            migrationBuilder.AddColumn<int>(
                name: "PeriodNum",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PeriodNum",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "RPrice",
                table: "Orders",
                newName: "Amount");
        }
    }
}
