using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class PlannedTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sugg-4 retires CategoryType.FutureExpense (3): a planned expense now
            // uses the very same Expense categories a hand-typed one does, which is
            // what lets a committed occurrence land in the budget and the report the
            // user already has. Any category still sitting on the retired value is
            // folded into Expense (0) rather than left pointing at an enum member
            // that no longer exists.
            migrationBuilder.Sql(@"UPDATE ""Categories"" SET ""CategoryType"" = 0 WHERE ""CategoryType"" = 3;");

            // Dropped, not migrated. Every FutureExpense was a one-off planned
            // expense with no account, which is now PlannedTransaction with
            // Recurrence = Once — but it cannot be rebuilt automatically: the new
            // row needs an Expense category (the old one was on the retired axis
            // folded away above) and neither table ever held the account the money
            // would come from. Confirmed with the owner that no rows exist.
            migrationBuilder.DropTable(
                name: "FutureExpenses");

            migrationBuilder.CreateTable(
                name: "PlannedTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SavingGoalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Recurrence = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedTransactions_Accounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlannedTransactions_Accounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlannedTransactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlannedTransactions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlannedTransactions_SavingGoals_SavingGoalId",
                        column: x => x.SavingGoalId,
                        principalTable: "SavingGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlannedOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncomeId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedOccurrences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlannedOccurrences_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlannedOccurrences_Incomes_IncomeId",
                        column: x => x.IncomeId,
                        principalTable: "Incomes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PlannedOccurrences_PlannedTransactions_PlannedTransactionId",
                        column: x => x.PlannedTransactionId,
                        principalTable: "PlannedTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlannedOccurrences_Transfers_TransferId",
                        column: x => x.TransferId,
                        principalTable: "Transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannedOccurrences_ExpenseId",
                table: "PlannedOccurrences",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedOccurrences_IncomeId",
                table: "PlannedOccurrences",
                column: "IncomeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedOccurrences_PlannedTransactionId_PeriodKey",
                table: "PlannedOccurrences",
                columns: new[] { "PlannedTransactionId", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlannedOccurrences_TransferId",
                table: "PlannedOccurrences",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedOccurrences_UserId_State_DueDate",
                table: "PlannedOccurrences",
                columns: new[] { "UserId", "State", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlannedTransactions_CategoryId",
                table: "PlannedTransactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedTransactions_FromAccountId",
                table: "PlannedTransactions",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedTransactions_SavingGoalId",
                table: "PlannedTransactions",
                column: "SavingGoalId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedTransactions_ToAccountId",
                table: "PlannedTransactions",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedTransactions_UserId_IsActive",
                table: "PlannedTransactions",
                columns: new[] { "UserId", "IsActive" });
        }

        /// <inheritdoc />
        /// <remarks>
        /// Rebuilds the FutureExpenses table but not its contents, and cannot tell
        /// which categories were on the retired type 3 before Up folded them into
        /// Expense — that distinction is gone for good. A down-migration here gets
        /// the schema back, not the data.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannedOccurrences");

            migrationBuilder.DropTable(
                name: "PlannedTransactions");

            migrationBuilder.CreateTable(
                name: "FutureExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PredictedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FutureExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FutureExpenses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FutureExpenses_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FutureExpenses_CategoryId",
                table: "FutureExpenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FutureExpenses_UserId_PredictedDate",
                table: "FutureExpenses",
                columns: new[] { "UserId", "PredictedDate" });
        }
    }
}
