using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Reimbursements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reimbursements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reimbursements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reimbursements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reimbursements_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReimbursementSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReimbursementId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReimbursementSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReimbursementSettlements_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReimbursementSettlements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReimbursementSettlements_Reimbursements_ReimbursementId",
                        column: x => x.ReimbursementId,
                        principalTable: "Reimbursements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reimbursements_ExpenseId",
                table: "Reimbursements",
                column: "ExpenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reimbursements_UserId",
                table: "Reimbursements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementSettlements_AccountId",
                table: "ReimbursementSettlements",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementSettlements_ReimbursementId",
                table: "ReimbursementSettlements",
                column: "ReimbursementId");

            migrationBuilder.CreateIndex(
                name: "IX_ReimbursementSettlements_UserId",
                table: "ReimbursementSettlements",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReimbursementSettlements");

            migrationBuilder.DropTable(
                name: "Reimbursements");
        }
    }
}
