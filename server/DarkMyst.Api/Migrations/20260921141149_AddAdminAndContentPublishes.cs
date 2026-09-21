using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DarkMyst.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAndContentPublishes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_accounts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    access_token = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "content_publishes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: true),
                    previous_version = table.Column<string>(type: "text", nullable: true),
                    published_by_admin_id = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_publishes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admin_accounts_access_token",
                table: "admin_accounts",
                column: "access_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_publishes_created_at",
                table: "content_publishes",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_accounts");

            migrationBuilder.DropTable(
                name: "content_publishes");
        }
    }
}
