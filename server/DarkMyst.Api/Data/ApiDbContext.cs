using DarkMyst.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DarkMyst.Api.Data
{
    /// <summary>
    /// The one EF Core context for the whole API. Kept to a single <c>DbContext</c> over a single
    /// schema, matching docs/01-architecture.md's "one backend, split into internal modules" — this
    /// is not a services-per-microservice split, just table ownership by feature folder.
    /// </summary>
    public sealed class ApiDbContext : DbContext
    {
        public ApiDbContext(DbContextOptions<ApiDbContext> options) : base(options)
        {
        }

        public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

        public DbSet<LinkedIdentityEntity> LinkedIdentities => Set<LinkedIdentityEntity>();

        public DbSet<PendingLinkEntity> PendingLinks => Set<PendingLinkEntity>();

        public DbSet<OwnedCharacterEntity> Characters => Set<OwnedCharacterEntity>();

        public DbSet<InventoryMaterialEntity> Materials => Set<InventoryMaterialEntity>();

        public DbSet<LedgerEntryEntity> Ledger => Set<LedgerEntryEntity>();

        public DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();

        public DbSet<EvolveHistoryEntity> EvolveHistory => Set<EvolveHistoryEntity>();

        public DbSet<ExpeditionRunEntity> ExpeditionRuns => Set<ExpeditionRunEntity>();

        public DbSet<SavedTeamEntity> SavedTeams => Set<SavedTeamEntity>();

        public DbSet<SavedTeamMemberEntity> SavedTeamMembers => Set<SavedTeamMemberEntity>();

        public DbSet<BattleChecksumMismatchEntity> BattleChecksumMismatches => Set<BattleChecksumMismatchEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AccountEntity>(b =>
            {
                b.ToTable("accounts");
                b.HasKey(x => x.Id);
                // Looked up on every request (bearer-token auth) — see AuthenticationHandler.
                b.HasIndex(x => x.AccessToken).IsUnique();
                b.Property(x => x.Kind).HasConversion<string>();
            });

            modelBuilder.Entity<LinkedIdentityEntity>(b =>
            {
                b.ToTable("linked_identities");
                b.HasKey(x => x.Id);
                b.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
                b.HasOne<AccountEntity>().WithMany().HasForeignKey(x => x.AccountId);
            });

            modelBuilder.Entity<PendingLinkEntity>(b =>
            {
                b.ToTable("pending_links");
                b.HasKey(x => x.Id);
            });

            modelBuilder.Entity<OwnedCharacterEntity>(b =>
            {
                b.ToTable("owned_characters");
                b.HasKey(x => x.InstanceId);
                b.HasIndex(x => x.OwnerId);
                b.Property(x => x.Focus).HasConversion<string>();
                b.HasOne<AccountEntity>().WithMany(a => a.Characters).HasForeignKey(x => x.OwnerId);
            });

            modelBuilder.Entity<InventoryMaterialEntity>(b =>
            {
                b.ToTable("inventory_materials");
                b.HasKey(x => new { x.AccountId, x.MaterialId });
                b.HasOne<AccountEntity>().WithMany(a => a.Materials).HasForeignKey(x => x.AccountId);
            });

            modelBuilder.Entity<LedgerEntryEntity>(b =>
            {
                b.ToTable("ledger_entries");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).UseIdentityAlwaysColumn();
                b.HasIndex(x => x.AccountId);
                b.Property(x => x.Kind).HasConversion<string>();
            });

            modelBuilder.Entity<IdempotencyRecordEntity>(b =>
            {
                b.ToTable("idempotency_records");
                b.HasKey(x => new { x.AccountId, x.Endpoint, x.Key });
                b.Property(x => x.Status).HasConversion<string>();
            });

            modelBuilder.Entity<EvolveHistoryEntity>(b =>
            {
                b.ToTable("evolve_history");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).UseIdentityAlwaysColumn();
                b.HasIndex(x => x.OwnerId);
            });

            modelBuilder.Entity<ExpeditionRunEntity>(b =>
            {
                b.ToTable("expedition_runs");
                b.HasKey(x => x.Id);
                b.HasIndex(x => x.OwnerId);
                b.Property(x => x.Lifecycle).HasConversion<string>();
                // Maps to Postgres's own system column: a free, always-changing row version, so a
                // second writer that raced past the explicit row lock still gets caught here.
                b.Property(x => x.Version).IsRowVersion();
            });

            modelBuilder.Entity<SavedTeamEntity>(b =>
            {
                b.ToTable("saved_teams");
                b.HasKey(x => x.Id);
                b.HasIndex(x => x.OwnerId);
                b.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SavedTeamMemberEntity>(b =>
            {
                b.ToTable("saved_team_members");
                b.HasKey(x => new { x.TeamId, x.Slot });
                b.HasIndex(x => x.InstanceId);
            });

            modelBuilder.Entity<BattleChecksumMismatchEntity>(b =>
            {
                b.ToTable("battle_checksum_mismatches");
                b.HasKey(x => x.Id);
                b.Property(x => x.Id).UseIdentityAlwaysColumn();
            });
        }
    }
}
