using System;
using System.Collections.Generic;
using DarkMyst.Combat.Model;

namespace DarkMyst.Content
{
    /// <summary>What an evolve would cost and produce. Also the preview shown before confirming.</summary>
    public sealed class EvolvePreview
    {
        public bool CanEvolve => Blockers.Count == 0;

        /// <summary>Human-readable reasons the evolve is refused. Empty when it is allowed.</summary>
        public List<string> Blockers { get; } = new List<string>();

        public string ResultCharacterId { get; set; }

        public int GoldCost { get; set; }

        public Dictionary<string, int> MaterialCost { get; set; } = new Dictionary<string, int>();

        /// <summary>Total essence the offered fodder is worth.</summary>
        public int Essence { get; set; }

        /// <summary>Inherited bonus this evolve would add, after the lifetime cap.</summary>
        public int GainedBonusPerMille { get; set; }

        /// <summary>Lifetime inherited bonus after the evolve.</summary>
        public int TotalBonusPerMille { get; set; }

        public StatBlock StatsBefore { get; set; }

        public StatBlock StatsAfter { get; set; }

        /// <summary>The owned character as it would be stored after a successful evolve.</summary>
        public OwnedCharacter Result { get; set; }
    }

    /// <summary>
    /// The evolve rules, as pure functions over content and owned characters.
    /// <para>
    /// Nothing here writes anything. The server calls <see cref="Preview"/> twice — once to
    /// render the confirm screen and again inside the transaction that actually spends the
    /// fodder — so a stale client cannot talk it into a result the current data does not
    /// support. See <c>docs/03-evolve-spec.md</c>.
    /// </para>
    /// </summary>
    public static class Evolution
    {
        /// <summary>
        /// Evaluates an evolve without performing it.
        /// </summary>
        /// <param name="pack">Content version the evolve is resolved against.</param>
        /// <param name="subject">The character being evolved.</param>
        /// <param name="fodder">Characters to be consumed. Must not contain the subject.</param>
        /// <param name="focus">Stat focus the player picked for this evolve.</param>
        public static EvolvePreview Preview(
            ContentPack pack,
            OwnedCharacter subject,
            IReadOnlyList<OwnedCharacter> fodder,
            EvolveFocus focus)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            if (subject == null)
            {
                throw new ArgumentNullException(nameof(subject));
            }

            fodder = fodder ?? new List<OwnedCharacter>();

            var preview = new EvolvePreview();
            CharacterData character = pack.GetCharacter(subject.CharacterId);
            EvolveRulesData rules = pack.Progression.Evolve;

            preview.StatsBefore = Progression.ComputeStats(pack, subject);

            if (string.IsNullOrEmpty(character.NextStageId))
            {
                preview.Blockers.Add(character.Name + " is already at its final stage.");
                return preview;
            }

            EvolveRequirementData requirement = rules.RequirementForStage(character.EvolveStage);
            if (requirement == null)
            {
                preview.Blockers.Add("No evolve requirement is defined for stage " + character.EvolveStage + ".");
                return preview;
            }

            preview.ResultCharacterId = character.NextStageId;
            preview.GoldCost = requirement.Gold;
            preview.MaterialCost = new Dictionary<string, int>(requirement.Materials);

            if (subject.Level < requirement.RequiredLevel)
            {
                preview.Blockers.Add(
                    character.Name + " must reach level " + requirement.RequiredLevel
                    + " (currently " + subject.Level + ").");
            }

            if (subject.IsLocked)
            {
                preview.Blockers.Add(character.Name + " is locked.");
            }

            ValidateFodder(pack, subject, fodder, character, requirement, preview);

            preview.Essence = TotalEssence(pack, character, fodder);

            int gained = rules.EssencePerBonusPerMille <= 0
                ? 0
                : preview.Essence / rules.EssencePerBonusPerMille;

            int total = subject.InheritedBonusPerMille + gained;
            if (total > rules.InheritedBonusCapPerMille)
            {
                total = rules.InheritedBonusCapPerMille;
            }

            preview.GainedBonusPerMille = total - subject.InheritedBonusPerMille;
            preview.TotalBonusPerMille = total;

            CharacterData next = pack.GetCharacter(character.NextStageId);
            preview.StatsAfter = Progression.ComputeStats(pack, next, 1, focus, total);

            OwnedCharacter result = subject.Clone();
            result.CharacterId = next.Id;
            result.Level = 1;
            result.Focus = focus;
            result.InheritedBonusPerMille = total;
            result.ContentVersion = pack.Version;
            preview.Result = result;

            return preview;
        }

        private static void ValidateFodder(
            ContentPack pack,
            OwnedCharacter subject,
            IReadOnlyList<OwnedCharacter> fodder,
            CharacterData character,
            EvolveRequirementData requirement,
            EvolvePreview preview)
        {
            if (fodder.Count != requirement.FodderCount)
            {
                preview.Blockers.Add(
                    "This evolve consumes exactly " + requirement.FodderCount
                    + " characters (" + fodder.Count + " offered).");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int sameLine = 0;

            foreach (OwnedCharacter unit in fodder)
            {
                if (unit.InstanceId == subject.InstanceId)
                {
                    preview.Blockers.Add("A character cannot be its own evolve material.");
                    continue;
                }

                if (!seen.Add(unit.InstanceId))
                {
                    preview.Blockers.Add("The same character was offered twice as material.");
                    continue;
                }

                if (unit.OwnerId != subject.OwnerId)
                {
                    preview.Blockers.Add("Material must belong to the same player.");
                    continue;
                }

                if (unit.IsLocked)
                {
                    preview.Blockers.Add(DescribeShort(pack, unit) + " is locked.");
                }

                if (unit.IsInUse)
                {
                    preview.Blockers.Add(DescribeShort(pack, unit) + " is in a team or an expedition.");
                }

                CharacterData fodderCharacter = pack.GetCharacter(unit.CharacterId);
                if (fodderCharacter.LineId == character.LineId)
                {
                    sameLine++;
                }
            }

            if (sameLine < requirement.SameLineFodderRequired)
            {
                preview.Blockers.Add(
                    "At least " + requirement.SameLineFodderRequired + " material must come from the "
                    + character.Name + " line (" + sameLine + " offered).");
            }
        }

        /// <summary>
        /// Essence a single fodder unit is worth. Level, rarity and evolve stage all count, and
        /// the share kept depends on how closely related it is: same line keeps everything,
        /// same affinity keeps half, anything else keeps whatever content says (0 by default).
        /// <para>
        /// This is what makes duplicates valuable without making them mandatory: a player who
        /// never rolls a second copy can still get most of the way there with same-affinity
        /// material, just slower.
        /// </para>
        /// </summary>
        public static int EssenceOf(ContentPack pack, CharacterData subjectCharacter, OwnedCharacter fodder)
        {
            EvolveRulesData rules = pack.Progression.Evolve;
            CharacterData fodderCharacter = pack.GetCharacter(fodder.CharacterId);

            long raw = (long)fodder.Level * rules.EssencePerLevel
                     + (long)(fodderCharacter.Rarity - 1) * rules.EssencePerRarityStep
                     + (long)fodderCharacter.EvolveStage * rules.EssencePerStage;

            int sharePerMille;
            if (fodderCharacter.LineId == subjectCharacter.LineId)
            {
                sharePerMille = rules.SameLineEssencePerMille;
                raw += rules.SameLineBonusEssence;
            }
            else if (fodderCharacter.Affinity == subjectCharacter.Affinity
                     && fodderCharacter.Affinity != Affinity.Neutral)
            {
                sharePerMille = rules.SameAffinityEssencePerMille;
            }
            else
            {
                sharePerMille = rules.OffAffinityEssencePerMille;
            }

            long value = raw * sharePerMille / 1000L;
            if (value < 0L)
            {
                return 0;
            }

            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        public static int TotalEssence(
            ContentPack pack, CharacterData subjectCharacter, IReadOnlyList<OwnedCharacter> fodder)
        {
            long total = 0;
            foreach (OwnedCharacter unit in fodder)
            {
                total += EssenceOf(pack, subjectCharacter, unit);
            }

            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private static string DescribeShort(ContentPack pack, OwnedCharacter owned)
        {
            try
            {
                return pack.GetCharacter(owned.CharacterId).Name;
            }
            catch (ContentException)
            {
                return owned.CharacterId;
            }
        }
    }
}
