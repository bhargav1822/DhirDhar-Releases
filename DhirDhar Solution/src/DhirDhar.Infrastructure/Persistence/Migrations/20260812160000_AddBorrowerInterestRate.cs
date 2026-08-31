using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DhirDhar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowerInterestRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InterestRate",
                table: "Borrowers",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterestRate",
                table: "Borrowers");
        }
    }
}
