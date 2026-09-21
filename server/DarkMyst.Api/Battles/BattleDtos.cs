using System.Collections.Generic;
using DarkMyst.Combat.Model;

namespace DarkMyst.Api.Battles
{
    public sealed record BattlePlacement(int Slot, string InstanceId);

    /// <summary>
    /// A training-ground fight (docs/04-economy-spec.md: "มีสนามทดลองที่คำนวณบนเครื่องได้ ...
    /// ไม่จ่ายรางวัล"). This endpoint never touches gold, materials or characters — it only ever
    /// returns a verifiable log, exactly what a client already computes locally when it lets a
    /// player try a team before committing to it in a real expedition node.
    /// </summary>
    public sealed record RunBattleRequest(
        string EncounterId, int LeaderSlot, List<BattlePlacement> Placements, string ClientChecksum);

    public sealed record RunBattleResponse(BattleResult Result, bool? ChecksumMatched);

    public sealed class BattleRefusedException : System.Exception
    {
        public BattleRefusedException(string message) : base(message)
        {
        }
    }
}
