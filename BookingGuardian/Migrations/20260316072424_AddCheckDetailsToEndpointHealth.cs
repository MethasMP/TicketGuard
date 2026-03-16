using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingGuardian.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckDetailsToEndpointHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckDetails",
                table: "endpoint_health",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckDetails",
                table: "endpoint_health");
        }
    }
}
