namespace DarkMyst.Api.Data.Entities
{
    /// <summary>
    /// One material stack for one account. Composite-keyed on (AccountId, MaterialId); a missing
    /// row means zero, the same convention <c>ExpeditionRunState.BankedMaterials</c> uses. Kept as
    /// a maintained balance, not something recomputed from the ledger on every read — see
    /// <see cref="Ledger.LedgerService"/> for why both are always written in the same transaction.
    /// </summary>
    public sealed class InventoryMaterialEntity
    {
        public string AccountId { get; set; }

        public string MaterialId { get; set; }

        public int Amount { get; set; }
    }
}
