using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    /// <summary>
    /// Stream V — savings envelopes. Additive throughout: one new column, two
    /// indexes, one foreign key, and a relaxation of SavingGoals.AccountId from
    /// required to optional. Nothing is dropped and no row is rewritten beyond
    /// the backfill below, which only fills in the column it just added.
    /// </summary>
    public partial class SavingsEnvelopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The goal's single backing account becomes optional rather than
            // going away: post-V goals do not need one, pre-V goals keep theirs
            // as the record of where their money used to sit — and as the
            // source the backfill three statements down reads from.
            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "SavingGoals",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "SavingContributions",
                type: "uuid",
                nullable: true);

            // Lossless backfill: every existing contribution is attributed to
            // the account its goal was backed by, which is exactly where that
            // money has been sitting all along. Run before the foreign key is
            // added, so the FK's own validation is the proof the backfill
            // produced only real account ids.
            migrationBuilder.Sql("""
                UPDATE "SavingContributions" AS sc
                SET "AccountId" = sg."AccountId"
                FROM "SavingGoals" AS sg
                WHERE sc."GoalId" = sg."Id"
                  AND sc."AccountId" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SavingContributions_AccountId",
                table: "SavingContributions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingContributions_UserId_AccountId",
                table: "SavingContributions",
                columns: new[] { "UserId", "AccountId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SavingContributions_Accounts_AccountId",
                table: "SavingContributions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Generated, and deliberately not a supported production path: it drops
        /// the column that says where reserved money lives, and forces goals
        /// created without an account back onto Guid.Empty. Rolling this back on
        /// live data means restoring a backup, not running Down.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SavingContributions_Accounts_AccountId",
                table: "SavingContributions");

            migrationBuilder.DropIndex(
                name: "IX_SavingContributions_AccountId",
                table: "SavingContributions");

            migrationBuilder.DropIndex(
                name: "IX_SavingContributions_UserId_AccountId",
                table: "SavingContributions");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "SavingContributions");

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "SavingGoals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
