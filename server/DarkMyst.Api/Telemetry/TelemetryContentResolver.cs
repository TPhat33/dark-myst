using DarkMyst.Api.Content;
using DarkMyst.Content;

namespace DarkMyst.Api.Telemetry
{
    /// <summary>Resolves the evolve-line identity of a character id for a telemetry payload,
    /// without ever letting a missing/retired content pack break the gameplay write it is
    /// attached to. Telemetry is a side channel (docs/08-metrics.md) — a lookup failure here
    /// degrades to a null/zeroed line rather than throwing, which is never true of the gameplay
    /// content lookups elsewhere in this API.</summary>
    public static class TelemetryContentResolver
    {
        public readonly struct LineInfo
        {
            public LineInfo(string lineId, int rarity, int evolveStage)
            {
                LineId = lineId;
                Rarity = rarity;
                EvolveStage = evolveStage;
            }

            public string LineId { get; }

            public int Rarity { get; }

            public int EvolveStage { get; }
        }

        public static LineInfo Resolve(ContentPackRegistry registry, string contentVersion, string characterId)
        {
            ContentPack pack;
            try
            {
                pack = registry.Get(contentVersion);
            }
            catch (ContentVersionUnavailableException)
            {
                // Deliberately no fallback to Latest here: that would stamp an event written under
                // one content version with a line looked up from a different one, silently — the
                // same "never mix versions" rule docs/10-backend-spec.md's admin read side holds
                // itself to. A null line is the honest answer, same as an unknown character id below.
                return new LineInfo(null, 0, 0);
            }

            try
            {
                CharacterData character = pack.GetCharacter(characterId);
                return new LineInfo(character.LineId, character.Rarity, character.EvolveStage);
            }
            catch (ContentException)
            {
                return new LineInfo(null, 0, 0);
            }
        }
    }
}
