namespace DarkMyst.Api.Debug
{
    public sealed record GrantCharacterRequest(string CharacterId, int Level, string Focus);

    public sealed record GrantGoldRequest(int Amount);

    /// <summary>Stand-in for a real gems purchase (see <c>Data/Entities/AccountEntity.cs</c>
    /// <c>Gems</c> remarks) — same shape and same gate as <see cref="GrantGoldRequest"/>.</summary>
    public sealed record GrantGemsRequest(int Amount);

    public sealed record GrantMaterialRequest(string MaterialId, int Amount);
}
