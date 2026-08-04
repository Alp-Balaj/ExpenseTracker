using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class PotLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SystemKind",
                table: "Categories",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PotExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaidByMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidFromAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PotExpenses_PotMemberships_CreatedByMembershipId",
                        column: x => x.CreatedByMembershipId,
                        principalTable: "PotMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotExpenses_PotMemberships_PaidByMembershipId",
                        column: x => x.PaidByMembershipId,
                        principalTable: "PotMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotExpenses_Pots_PotId",
                        column: x => x.PotId,
                        principalTable: "Pots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PotTopUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PotId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotTopUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PotTopUps_PotMemberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "PotMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotTopUps_Pots_PotId",
                        column: x => x.PotId,
                        principalTable: "Pots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PotWithdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PotId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DestinationAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotWithdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PotWithdrawals_PotMemberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "PotMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotWithdrawals_Pots_PotId",
                        column: x => x.PotId,
                        principalTable: "Pots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PotExpenseShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PotId = table.Column<Guid>(type: "uuid", nullable: false),
                    PotExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotExpenseShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PotExpenseShares_PotExpenses_PotExpenseId",
                        column: x => x.PotExpenseId,
                        principalTable: "PotExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PotExpenseShares_PotMemberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "PotMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotExpenseShares_Pots_PotId",
                        column: x => x.PotId,
                        principalTable: "Pots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PotExpenses_CreatedByMembershipId",
                table: "PotExpenses",
                column: "CreatedByMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PotExpenses_PaidByMembershipId",
                table: "PotExpenses",
                column: "PaidByMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PotExpenses_PotId_Date",
                table: "PotExpenses",
                columns: new[] { "PotId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PotExpenseShares_CategoryId",
                table: "PotExpenseShares",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PotExpenseShares_MembershipId",
                table: "PotExpenseShares",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PotExpenseShares_PotExpenseId_MembershipId",
                table: "PotExpenseShares",
                columns: new[] { "PotExpenseId", "MembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PotExpenseShares_PotId",
                table: "PotExpenseShares",
                column: "PotId");

            migrationBuilder.CreateIndex(
                name: "IX_PotTopUps_MembershipId",
                table: "PotTopUps",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PotTopUps_PotId_Date",
                table: "PotTopUps",
                columns: new[] { "PotId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PotWithdrawals_MembershipId",
                table: "PotWithdrawals",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PotWithdrawals_PotId_Date",
                table: "PotWithdrawals",
                columns: new[] { "PotId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PotExpenseShares");

            migrationBuilder.DropTable(
                name: "PotTopUps");

            migrationBuilder.DropTable(
                name: "PotWithdrawals");

            migrationBuilder.DropTable(
                name: "PotExpenses");

            migrationBuilder.DropColumn(
                name: "SystemKind",
                table: "Categories");
        }
    }
}
