using System.Collections.Generic;
using System.Text;

namespace DarkMyst.Combat.Model
{
    public sealed class UnitSnapshot
    {
        public UnitRef Ref { get; set; }

        public string InstanceId { get; set; }

        public string CharacterId { get; set; }

        public int Hp { get; set; }

        public int MaxHp { get; set; }

        public bool Alive { get; set; }
    }

    /// <summary>
    /// The full outcome of one battle. The server stores this; the client replays
    /// <see cref="Events"/> to show it.
    /// </summary>
    public sealed class BattleResult
    {
        public BattleOutcome Outcome { get; set; }

        public BattleEndReason EndReason { get; set; }

        /// <summary>Number of rounds that actually ran.</summary>
        public int Rounds { get; set; }

        public ulong Seed { get; set; }

        public string RulesVersion { get; set; }

        public string ContentVersion { get; set; }

        /// <summary>
        /// How many 32-bit words the simulation drew. A client and a server that agree on the
        /// outcome but disagree here have diverged somewhere harmless today and dangerous later.
        /// </summary>
        public long RngCalls { get; set; }

        public List<BattleEvent> Events { get; set; } = new List<BattleEvent>();

        public List<UnitSnapshot> FinalUnits { get; set; } = new List<UnitSnapshot>();

        /// <summary>
        /// FNV-1a 64 over the canonical form of the event log. Comparing this one string is
        /// enough to tell whether two runs of the same request agreed.
        /// </summary>
        public string Checksum { get; set; }

        /// <summary>Computes the checksum over the current event list.</summary>
        public string ComputeChecksum()
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offsetBasis;
            foreach (BattleEvent evt in Events)
            {
                string canonical = evt.ToCanonicalString();
                foreach (byte b in Encoding.UTF8.GetBytes(canonical))
                {
                    unchecked
                    {
                        hash ^= b;
                        hash *= prime;
                    }
                }

                unchecked
                {
                    hash ^= (byte)'\n';
                    hash *= prime;
                }
            }

            return hash.ToString("x16");
        }
    }
}
