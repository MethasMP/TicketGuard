using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingGuardian.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerEmailToExistingTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "bookings",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "bookings");
        }
    }
}
