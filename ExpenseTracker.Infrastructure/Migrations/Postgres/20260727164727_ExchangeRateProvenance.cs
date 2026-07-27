using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ExchangeRateProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RateUpdatedAtUtc",
                table: "Currencies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_UserId_Code",
                table: "Currencies",
                columns: new[] { "UserId", "Code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Currencies_UserId_Code",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "RateUpdatedAtUtc",
                table: "Currencies");
        }
    }
}
