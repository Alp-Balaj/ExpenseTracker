using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class OAuthForMcp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "McpTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthClientId",
                table: "McpTokens",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshExpiresAtUtc",
                table: "McpTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshTokenHash",
                table: "McpTokens",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OAuthAuthorizationCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ClientName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RedirectUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CodeChallenge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CanWrite = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAuthorizationCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OAuthAuthorizationCodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OAuthClients",
                columns: table => new
                {
                    ClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientSecretHash = table.Column<string>(type: "text", nullable: true),
                    ClientName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RedirectUris = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthClients", x => x.ClientId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpTokens_RefreshTokenHash",
                table: "McpTokens",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationCodes_CodeHash",
                table: "OAuthAuthorizationCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAuthorizationCodes_UserId",
                table: "OAuthAuthorizationCodes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OAuthAuthorizationCodes");

            migrationBuilder.DropTable(
                name: "OAuthClients");

            migrationBuilder.DropIndex(
                name: "IX_McpTokens_RefreshTokenHash",
                table: "McpTokens");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "McpTokens");

            migrationBuilder.DropColumn(
                name: "OAuthClientId",
                table: "McpTokens");

            migrationBuilder.DropColumn(
                name: "RefreshExpiresAtUtc",
                table: "McpTokens");

            migrationBuilder.DropColumn(
                name: "RefreshTokenHash",
                table: "McpTokens");
        }
    }
}
