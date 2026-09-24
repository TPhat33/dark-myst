using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DarkMyst.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSummonAndGems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "gems",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "account_lines",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    line_id = table.Column<string>(type: "text", nullable: false),
                    first_obtained_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_lines", x => new { x.account_id, x.line_id });
                    table.ForeignKey(
                        name: "fk_account_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "echo_shards",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    line_id = table.Column<string>(type: "text", nullable: false),
                    shard_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_echo_shards", x => new { x.account_id, x.line_id });
                    table.ForeignKey(
                        name: "fk_echo_shards_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "summon_state",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    pulls_since_last_pity = table.Column<int>(type: "integer", nullable: false),
                    pulls_since_last_floor = table.Column<int>(type: "integer", nullable: false),
                    spark_points = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_summon_state", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_summon_state_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_lines");

            migrationBuilder.DropTable(
                name: "echo_shards");

            migrationBuilder.DropTable(
                name: "summon_state");

            migrationBuilder.DropColumn(
                name: "gems",
                table: "accounts");
        }
    }
}
