using System.Collections.Generic;
using DarkMyst.Combat.Model;
using Xunit;

namespace DarkMyst.Content.Tests
{
    /// <summary>
    /// Evolve is where a player's time is actually spent, so these rules are pinned down
    /// before any of it is built: what it costs, what it inherits, and every case where the
    /// server must refuse.
    /// </summary>
    public class EvolutionTests
    {
        private const string Owner = "player_1";

        private static OwnedCharacter Owned(
            string instanceId,
            string characterId,
            int level = 1,
            bool locked = false,
            bool inUse = false,
            string owner = Owner)
        {
            return new OwnedCharacter
            {
                InstanceId = instanceId,
                OwnerId = owner,
                CharacterId = characterId,
                Level = level,
                ContentVersion = "0.1.0",
                IsLocked = locked,
                IsInUse = inUse
            };
        }

        private static ContentPack Pack => ShippedContent.Instance;

        // ------------------------------------------------------------------
        // The happy path
        // ------------------------------------------------------------------

        [Fact]
        public void A_valid_evolve_reports_its_cost_and_its_result()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i", level: 10),
                Owned("own_3", "chr_ember_adept_i", level: 10)
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.Guard);

            Assert.True(preview.CanEvolve, string.Join("; ", preview.Blockers));
            Assert.Equal("chr_ashen_knight_ii", preview.ResultCharacterId);
            Assert.Equal(5000, preview.GoldCost);
            Assert.Equal(3, preview.MaterialCost["mat_ashen_sigil"]);

            // Same affinity, different line: (10 level * 10) + (rarity 3 - 1) * 50 + stage 1 * 100
            // = 300, halved to 150 each.
            Assert.Equal(300, preview.Essence);
            Assert.Equal(30, preview.GainedBonusPerMille);

            Assert.Equal("chr_ashen_knight_ii", preview.Result.CharacterId);
            Assert.Equal(1, preview.Result.Level);
            Assert.Equal(EvolveFocus.Guard, preview.Result.Focus);
            Assert.Equal(30, preview.Result.InheritedBonusPerMille);
        }

        [Fact]
        public void Same_line_material_is_worth_more_than_same_affinity_material()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);

            var sameAffinity = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i", level: 10),
                Owned("own_3", "chr_ember_adept_i", level: 10)
            };

            var sameLine = new List<OwnedCharacter>
            {
                Owned("own_4", "chr_ashen_knight_i", level: 10),
                Owned("own_5", "chr_ashen_knight_i", level: 10)
            };

            int affinityEssence = Evolution.Preview(Pack, subject, sameAffinity, EvolveFocus.None).Essence;
            int lineEssence = Evolution.Preview(Pack, subject, sameLine, EvolveFocus.None).Essence;

            Assert.True(lineEssence > affinityEssence);
        }

        [Fact]
        public void Unrelated_material_still_satisfies_the_count_but_grants_nothing()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_tide_oracle_i", level: 15),
                Owned("own_3", "chr_dawn_cantor_i", level: 15)
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.None);

            Assert.True(preview.CanEvolve, string.Join("; ", preview.Blockers));
            Assert.Equal(0, preview.Essence);
            Assert.Equal(0, preview.GainedBonusPerMille);
        }

        [Fact]
        public void The_preview_stats_are_exactly_what_the_result_would_compute()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ashen_knight_i", level: 20),
                Owned("own_3", "chr_ashen_knight_i", level: 20)
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.Offense);

            StatBlock recomputed = Progression.ComputeStats(Pack, preview.Result);
            Assert.Equal(preview.StatsAfter, recomputed);
        }

        [Fact]
        public void The_lifetime_bonus_is_capped_across_repeated_evolves()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            subject.InheritedBonusPerMille = 290;

            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ashen_knight_i", level: 20),
                Owned("own_3", "chr_ashen_knight_i", level: 20)
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.None);

            Assert.Equal(300, preview.TotalBonusPerMille);
            Assert.Equal(10, preview.GainedBonusPerMille);
        }

        // ------------------------------------------------------------------
        // Everything the server has to refuse
        // ------------------------------------------------------------------

        [Fact]
        public void An_under_levelled_character_cannot_evolve()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 19);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i"),
                Owned("own_3", "chr_ember_adept_i")
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.None);

            Assert.False(preview.CanEvolve);
            Assert.Contains(preview.Blockers, b => b.Contains("level 20"));
        }

        [Fact]
        public void The_wrong_amount_of_material_is_refused()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);

            EvolvePreview tooFew = Evolution.Preview(
                Pack, subject, new List<OwnedCharacter> { Owned("own_2", "chr_ember_adept_i") }, EvolveFocus.None);

            Assert.False(tooFew.CanEvolve);
            Assert.Contains(tooFew.Blockers, b => b.Contains("exactly 2"));
        }

        [Fact]
        public void A_locked_character_can_neither_evolve_nor_be_spent()
        {
            OwnedCharacter lockedSubject = Owned("own_1", "chr_ashen_knight_i", level: 20, locked: true);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i"),
                Owned("own_3", "chr_ember_adept_i")
            };

            Assert.Contains(
                Evolution.Preview(Pack, lockedSubject, fodder, EvolveFocus.None).Blockers,
                b => b.Contains("locked"));

            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var lockedFodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i", locked: true),
                Owned("own_3", "chr_ember_adept_i")
            };

            Assert.Contains(
                Evolution.Preview(Pack, subject, lockedFodder, EvolveFocus.None).Blockers,
                b => b.Contains("locked"));
        }

        [Fact]
        public void Material_currently_in_a_team_or_an_expedition_is_refused()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i", inUse: true),
                Owned("own_3", "chr_ember_adept_i")
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.None);

            Assert.False(preview.CanEvolve);
            Assert.Contains(preview.Blockers, b => b.Contains("in a team or an expedition"));
        }

        [Fact]
        public void A_character_cannot_be_its_own_material()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_1", "chr_ashen_knight_i"),
                Owned("own_3", "chr_ember_adept_i")
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.None);

            Assert.False(preview.CanEvolve);
            Assert.Contains(preview.Blockers, b => b.Contains("its own evolve material"));
        }

        [Fact]
        public void The_same_material_cannot_be_submitted_twice()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i"),
                Owned("own_2", "chr_ember_adept_i")
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.None);

            Assert.False(preview.CanEvolve);
            Assert.Contains(preview.Blockers, b => b.Contains("offered twice"));
        }

        [Fact]
        public void Material_belonging_to_someone_else_is_refused()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i", owner: "player_2"),
                Owned("own_3", "chr_ember_adept_i")
            };

            EvolvePreview preview = Evolution.Preview(Pack, subject, fodder, EvolveFocus.None);

            Assert.False(preview.CanEvolve);
            Assert.Contains(preview.Blockers, b => b.Contains("same player"));
        }

        [Fact]
        public void A_final_stage_character_cannot_evolve_further()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_iii", level: 60);

            EvolvePreview preview = Evolution.Preview(Pack, subject, new List<OwnedCharacter>(), EvolveFocus.None);

            Assert.False(preview.CanEvolve);
            Assert.Contains(preview.Blockers, b => b.Contains("final stage"));
        }

        [Fact]
        public void Stage_two_demands_material_from_the_same_line()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_ii", level: 40);
            var offLine = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ember_adept_i", level: 20),
                Owned("own_3", "chr_ember_adept_i", level: 20),
                Owned("own_4", "chr_ember_adept_i", level: 20)
            };

            EvolvePreview refused = Evolution.Preview(Pack, subject, offLine, EvolveFocus.None);
            Assert.False(refused.CanEvolve);
            Assert.Contains(refused.Blockers, b => b.Contains("Ashen Templar line"));

            var withLine = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ashen_knight_i", level: 20),
                Owned("own_3", "chr_ember_adept_i", level: 20),
                Owned("own_4", "chr_ember_adept_i", level: 20)
            };

            EvolvePreview allowed = Evolution.Preview(Pack, subject, withLine, EvolveFocus.None);
            Assert.True(allowed.CanEvolve, string.Join("; ", allowed.Blockers));
            Assert.Equal(25000, allowed.GoldCost);
        }

        [Fact]
        public void Preview_never_mutates_the_characters_it_was_given()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ashen_knight_i", level: 20),
                Owned("own_3", "chr_ashen_knight_i", level: 20)
            };

            Evolution.Preview(Pack, subject, fodder, EvolveFocus.Offense);

            Assert.Equal("chr_ashen_knight_i", subject.CharacterId);
            Assert.Equal(20, subject.Level);
            Assert.Equal(EvolveFocus.None, subject.Focus);
            Assert.Equal(0, subject.InheritedBonusPerMille);
        }

        [Fact]
        public void Preview_is_stable_so_the_confirm_step_agrees_with_the_screen()
        {
            OwnedCharacter subject = Owned("own_1", "chr_ashen_knight_i", level: 20);
            var fodder = new List<OwnedCharacter>
            {
                Owned("own_2", "chr_ashen_knight_i", level: 17),
                Owned("own_3", "chr_ember_adept_i", level: 12)
            };

            EvolvePreview shown = Evolution.Preview(Pack, subject, fodder, EvolveFocus.Arcane);
            EvolvePreview confirmed = Evolution.Preview(Pack, subject, fodder, EvolveFocus.Arcane);

            Assert.Equal(shown.Essence, confirmed.Essence);
            Assert.Equal(shown.TotalBonusPerMille, confirmed.TotalBonusPerMille);
            Assert.Equal(shown.StatsAfter, confirmed.StatsAfter);
        }
    }
}
