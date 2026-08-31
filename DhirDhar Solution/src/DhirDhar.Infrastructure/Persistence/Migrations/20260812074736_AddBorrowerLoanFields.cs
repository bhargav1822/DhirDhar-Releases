using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DhirDhar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowerLoanFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BorrowerPhotoPath",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrnamentPhotoPath",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoanType",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrnamentType",
                table: "Borrowers",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OrnamentWeight",
                table: "Borrowers",
                type: "REAL",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LoanAmount",
                table: "Borrowers",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoanDate",
                table: "Borrowers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BorrowerPhotoPath",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "OrnamentPhotoPath",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "LoanType",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "OrnamentType",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "OrnamentWeight",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "LoanAmount",
                table: "Borrowers");

            migrationBuilder.DropColumn(
                name: "LoanDate",
                table: "Borrowers");
        }
    }
}
