namespace DarkMyst.Api.Debug
{
    public sealed record GrantCharacterRequest(string CharacterId, int Level, string Focus);

    public sealed record GrantGoldRequest(int Amount);

    public sealed record GrantMaterialRequest(string MaterialId, int Amount);
}
