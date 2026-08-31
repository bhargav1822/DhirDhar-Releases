using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DhirDhar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowerProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AadharNumber",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FatherName",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Surname",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Village",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AadharNumber",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "FatherName",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "Surname",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "Village",
                table: "Borrowers");
        }
    }
}
