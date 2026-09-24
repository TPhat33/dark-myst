namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// Accumulated Echo shard balance for one account, one line (docs/12-summon-spec.md
    /// §"ตัวซ้ำต้องไม่มีวันเป็นของเหลือ"). Gained by a duplicate summon/spark-redeem pull of a line
    /// the account already owns (<see cref="DarkMyst.Sim.SummonRules.DuplicateShardsForRarity"/>),
    /// spent by <c>POST /summon/attune</c> to raise that line's owned characters'
    /// <see cref="OwnedCharacterEntity.InheritedBonusPerMille"/>. A row is created on first
    /// duplicate and never deleted, even if it reaches 0 — same "0 is a normal balance, not a
    /// missing row" convention <see cref="InventoryMaterialEntity"/> already uses.
    /// </summary>
    public sealed class EchoShardEntity
    {
        public string AccountId { get; set; }

        public string LineId { get; set; }

        public int ShardCount { get; set; }
    }
}
