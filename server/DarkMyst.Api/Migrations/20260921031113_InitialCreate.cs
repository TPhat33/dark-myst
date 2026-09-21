using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DarkMyst.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    gold = table.Column<int>(type: "integer", nullable: false),
                    access_token = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "battle_checksum_mismatches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<string>(type: "text", nullable: true),
                    encounter_id = table.Column<string>(type: "text", nullable: true),
                    server_checksum = table.Column<string>(type: "text", nullable: true),
                    client_checksum = table.Column<string>(type: "text", nullable: true),
                    request_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_battle_checksum_mismatches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evolve_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    owner_id = table.Column<string>(type: "text", nullable: true),
                    subject_instance_id = table.Column<string>(type: "text", nullable: true),
                    from_character_id = table.Column<string>(type: "text", nullable: true),
                    result_character_id = table.Column<string>(type: "text", nullable: true),
                    result_instance_id = table.Column<string>(type: "text", nullable: true),
                    fodder_instance_ids_json = table.Column<string>(type: "text", nullable: true),
                    focus = table.Column<string>(type: "text", nullable: true),
                    content_version = table.Column<string>(type: "text", nullable: true),
                    gold_spent = table.Column<int>(type: "integer", nullable: false),
                    materials_spent_json = table.Column<string>(type: "text", nullable: true),
                    gained_bonus_per_mille = table.Column<int>(type: "integer", nullable: false),
                    total_bonus_per_mille = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evolve_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expedition_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: true),
                    stage_id = table.Column<string>(type: "text", nullable: true),
                    content_version = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    lifecycle = table.Column<string>(type: "text", nullable: false),
                    state_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expedition_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => new { x.account_id, x.endpoint, x.key });
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    account_id = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: true),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_links",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    guest_account_id = table.Column<string>(type: "text", nullable: true),
                    existing_account_id = table.Column<string>(type: "text", nullable: true),
                    provider = table.Column<string>(type: "text", nullable: true),
                    external_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_teams",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    leader_slot = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_teams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_materials",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    material_id = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_materials", x => new { x.account_id, x.material_id });
                    table.ForeignKey(
                        name: "fk_inventory_materials_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "linked_identities",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: true),
                    external_id = table.Column<string>(type: "text", nullable: true),
                    account_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linked_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_linked_identities_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "owned_characters",
                columns: table => new
                {
                    instance_id = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: true),
                    character_id = table.Column<string>(type: "text", nullable: true),
                    level = table.Column<int>(type: "integer", nullable: false),
                    focus = table.Column<string>(type: "text", nullable: false),
                    inherited_bonus_per_mille = table.Column<int>(type: "integer", nullable: false),
                    content_version = table.Column<string>(type: "text", nullable: true),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    is_in_use = table.Column<bool>(type: "boolean", nullable: false),
                    obtained_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owned_characters", x => x.instance_id);
                    table.ForeignKey(
                        name: "fk_owned_characters_accounts_owner_id",
                        column: x => x.owner_id,
                        principalTable: "accounts",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "saved_team_members",
                columns: table => new
                {
                    team_id = table.Column<string>(type: "text", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    instance_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_team_members", x => new { x.team_id, x.slot });
                    table.ForeignKey(
                        name: "fk_saved_team_members_saved_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "saved_teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_access_token",
                table: "accounts",
                column: "access_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evolve_history_owner_id",
                table: "evolve_history",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_expedition_runs_owner_id",
                table: "expedition_runs",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account_id",
                table: "ledger_entries",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_identities_account_id",
                table: "linked_identities",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_identities_provider_external_id",
                table: "linked_identities",
                columns: new[] { "provider", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_owned_characters_owner_id",
                table: "owned_characters",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_team_members_instance_id",
                table: "saved_team_members",
                column: "instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_teams_owner_id",
                table: "saved_teams",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "battle_checksum_mismatches");

            migrationBuilder.DropTable(
                name: "evolve_history");

            migrationBuilder.DropTable(
                name: "expedition_runs");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "inventory_materials");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "linked_identities");

            migrationBuilder.DropTable(
                name: "owned_characters");

            migrationBuilder.DropTable(
                name: "pending_links");

            migrationBuilder.DropTable(
                name: "saved_team_members");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "saved_teams");
        }
    }
}
