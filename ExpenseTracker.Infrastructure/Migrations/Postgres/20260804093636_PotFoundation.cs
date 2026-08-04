using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class PotFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    LowBalanceThreshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pots_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PotMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PotId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LeftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AnonymizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PotMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotMemberships_Pots_PotId",
                        column: x => x.PotId,
                        principalTable: "Pots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PotActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Before = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    After = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PotActivities_PotMemberships_ActorMembershipId",
                        column: x => x.ActorMembershipId,
                        principalTable: "PotMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotActivities_Pots_PotId",
                        column: x => x.PotId,
                        principalTable: "Pots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PotInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByMembershipId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PotInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PotInvites_PotMemberships_IssuedByMembershipId",
                        column: x => x.IssuedByMembershipId,
                        principalTable: "PotMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotInvites_PotMemberships_RevokedByMembershipId",
                        column: x => x.RevokedByMembershipId,
                        principalTable: "PotMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PotInvites_Pots_PotId",
                        column: x => x.PotId,
                        principalTable: "Pots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PotActivities_ActorMembershipId",
                table: "PotActivities",
                column: "ActorMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PotActivities_PotId_CreatedAt",
                table: "PotActivities",
                columns: new[] { "PotId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PotInvites_Code",
                table: "PotInvites",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PotInvites_IssuedByMembershipId",
                table: "PotInvites",
                column: "IssuedByMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PotInvites_PotId_CreatedAt",
                table: "PotInvites",
                columns: new[] { "PotId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PotInvites_RevokedByMembershipId",
                table: "PotInvites",
                column: "RevokedByMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_PotMemberships_PotId_UserId",
                table: "PotMemberships",
                columns: new[] { "PotId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PotMemberships_UserId_LeftAt",
                table: "PotMemberships",
                columns: new[] { "UserId", "LeftAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Pots_CreatedByUserId",
                table: "Pots",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PotActivities");

            migrationBuilder.DropTable(
                name: "PotInvites");

            migrationBuilder.DropTable(
                name: "PotMemberships");

            migrationBuilder.DropTable(
                name: "Pots");
        }
    }
}
