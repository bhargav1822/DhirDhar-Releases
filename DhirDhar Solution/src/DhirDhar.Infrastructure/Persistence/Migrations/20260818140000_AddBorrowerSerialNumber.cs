using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DhirDhar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowerSerialNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "Borrowers");
        }
    }
}
