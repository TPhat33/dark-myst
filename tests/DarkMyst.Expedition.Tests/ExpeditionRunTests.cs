using System.Collections.Generic;
using System.Text.RegularExpressions;
using DarkMyst.Combat;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using DarkMyst.Expedition.Model;
using Xunit;

namespace DarkMyst.Expedition.Tests
{
    public class ExpeditionRunTests
    {
        // ------------------------------------------------------------------
        // Map generation
        // ------------------------------------------------------------------

        [Fact]
        public void The_same_seed_generates_the_identical_map()
        {
            ContentPack pack = TestContent.Load();

            ExpeditionRun a = ExpeditionRun.Start(pack, "stg_test_map", 12345, 0, TestContent.HeroPlacement());
            ExpeditionRun b = ExpeditionRun.Start(pack, "stg_test_map", 12345, 0, TestContent.HeroPlacement());

            Assert.Equal(a.State.EntryNodeIds, b.State.EntryNodeIds);
            Assert.Equal(MapSignature(a), MapSignature(b));
        }

        [Fact]
        public void A_different_seed_produces_a_different_map()
        {
            ContentPack pack = TestContent.Load();

            ExpeditionRun a = ExpeditionRun.Start(pack, "stg_test_map", 1, 0, TestContent.HeroPlacement());
            ExpeditionRun b = ExpeditionRun.Start(pack, "stg_test_map", 2, 0, TestContent.HeroPlacement());

            Assert.NotEqual(MapSignature(a), MapSignature(b));
        }

        [Fact]
        public void Every_node_has_at_least_one_way_forward_and_every_node_is_reachable()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_map", 777, 0, TestContent.HeroPlacement());

            var reachable = new HashSet<int>(run.State.EntryNodeIds);
            var frontier = new Queue<int>(run.State.EntryNodeIds);
            while (frontier.Count > 0)
            {
                ExpeditionNodeState node = run.State.Nodes[frontier.Dequeue()];
                if (node.Kind != NodeKind.Boss)
                {
                    Assert.NotEmpty(node.NextNodeIds);
                }

                foreach (int next in node.NextNodeIds)
                {
                    if (reachable.Add(next))
                    {
                        frontier.Enqueue(next);
                    }
                }
            }

            Assert.Equal(run.State.Nodes.Count, reachable.Count);
        }

        private static string MapSignature(ExpeditionRun run)
        {
            var parts = new List<string>();
            foreach (ExpeditionNodeState node in run.State.Nodes)
            {
                parts.Add(node.Kind + ":" + (node.RefId ?? "-") + ":" + string.Join(",", node.NextNodeIds));
            }

            return string.Join("|", parts);
        }

        // ------------------------------------------------------------------
        // Determinism across a full run
        // ------------------------------------------------------------------

        [Fact]
        public void A_full_playthrough_is_reproducible_from_the_seed()
        {
            ContentPack pack = TestContent.Load();

            ExpeditionRun a = PlayFully(pack, "stg_test_linear_win", 999);
            ExpeditionRun b = PlayFully(pack, "stg_test_linear_win", 999);

            Assert.Equal(a.State.ComputeChecksum(), b.State.ComputeChecksum());
            Assert.Equal(a.State.BankedGold, b.State.BankedGold);
            Assert.Equal(a.State.Status, b.State.Status);
        }

        [Fact]
        public void Each_node_in_a_run_battles_with_its_own_seed()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_linear_win", 555, 0, TestContent.HeroPlacement());

            var checksums = new List<string>();
            while (run.AvailableChoices().Count > 0)
            {
                ExpeditionNodeOutcome outcome = run.Choose(pack, 0);
                Assert.NotNull(outcome.BattleResult);
                checksums.Add(outcome.BattleResult.Checksum);
            }

            Assert.True(checksums.Count >= 2);
            Assert.True(
                new HashSet<string>(checksums).Count > 1,
                "Every node's battle produced the identical checksum, so nodes are not seeded independently.");
        }

        private static ExpeditionRun PlayFully(ContentPack pack, string stageId, ulong seed)
        {
            ExpeditionRun run = ExpeditionRun.Start(pack, stageId, seed, 0, TestContent.HeroPlacement());
            while (run.AvailableChoices().Count > 0)
            {
                run.Choose(pack, 0);
            }

            return run;
        }

        // ------------------------------------------------------------------
        // HP carry-over
        // ------------------------------------------------------------------

        [Fact]
        public void Hp_carries_over_between_battle_nodes()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_linear_win", 42, 0, TestContent.HeroPlacement());

            run.Choose(pack, 0);
            int hpAfterFirst = run.State.Team[0].CurrentHp;
            Assert.True(hpAfterFirst < run.State.Team[0].MaxHp, "The hero took no damage from a live enemy.");

            ExpeditionNodeOutcome second = run.Choose(pack, 0);

            // Reconstruct the HP the second battle started at from its own log: the first hit
            // against the hero says how much was taken and what HP remained afterwards.
            BattleEvent firstHitOnHero = FindFirstHitOn(second.BattleResult, TeamSide.Attacker);
            int startingHpOfSecondBattle = firstHitOnHero.TargetHpAfter + firstHitOnHero.Amount;

            Assert.Equal(hpAfterFirst, startingHpOfSecondBattle);
        }

        [Fact]
        public void A_rest_node_restores_a_share_of_missing_hp_and_nothing_more()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_rest", 3, 0, TestContent.HeroPlacement());

            run.Choose(pack, 0); // battle: hero takes damage from the faster enemy
            int hpBeforeRest = run.State.Team[0].CurrentHp;
            int maxHp = run.State.Team[0].MaxHp;
            Assert.True(hpBeforeRest < maxHp);

            ExpeditionNodeOutcome restOutcome = run.Choose(pack, 0);
            Assert.Equal(500, restOutcome.HealedPerMille);

            int expectedHeal = (maxHp - hpBeforeRest) * 500 / 1000;
            Assert.Equal(hpBeforeRest + expectedHeal, run.State.Team[0].CurrentHp);

            ExpeditionNodeOutcome third = run.Choose(pack, 0);
            BattleEvent firstHitOnHero = FindFirstHitOn(third.BattleResult, TeamSide.Attacker);
            Assert.Equal(hpBeforeRest + expectedHeal, firstHitOnHero.TargetHpAfter + firstHitOnHero.Amount);
        }

        private static BattleEvent FindFirstHitOn(BattleResult result, TeamSide side)
        {
            foreach (BattleEvent evt in result.Events)
            {
                if (evt.Kind == BattleEventKind.Damaged && evt.Target.HasValue && evt.Target.Value.Side == side)
                {
                    return evt;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Run-scoped buffs
        // ------------------------------------------------------------------

        [Fact]
        public void A_run_scoped_buff_applies_to_a_later_battle_in_the_same_run()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_event_buff", 7, 0, TestContent.HeroPlacement());

            ExpeditionNodeOutcome eventOutcome = run.Choose(pack, 0);
            Assert.Equal(TestContent.RelicBuffSkillId, eventOutcome.GrantedBuffSkillId);
            Assert.Contains(TestContent.RelicBuffSkillId, run.State.RunBuffSkillIds);

            ExpeditionNodeOutcome bossOutcome = run.Choose(pack, 0);
            Assert.Contains(
                bossOutcome.BattleResult.Events,
                e => e.Kind == BattleEventKind.StatusApplied && e.StatusId == TestContent.RelicBuffStatusId);
        }

        [Fact]
        public void A_run_scoped_buff_does_not_survive_the_run_it_was_granted_in()
        {
            ContentPack pack = TestContent.Load();

            ExpeditionRun buffed = ExpeditionRun.Start(pack, "stg_test_event_buff", 7, 0, TestContent.HeroPlacement());
            buffed.Choose(pack, 0);
            buffed.Choose(pack, 0);
            Assert.NotEmpty(buffed.State.RunBuffSkillIds);

            // A brand new run with the very same hero never sees it.
            ExpeditionRun fresh = ExpeditionRun.Start(pack, "stg_test_linear_win", 7, 0, TestContent.HeroPlacement());
            ExpeditionNodeOutcome outcome = fresh.Choose(pack, 0);

            Assert.Empty(fresh.State.RunBuffSkillIds);
            Assert.DoesNotContain(
                outcome.BattleResult.Events, e => e.StatusId == TestContent.RelicBuffStatusId);

            // The character definition itself was never touched — the buff was never anything
            // more than an entry in the (now discarded) run's own state.
            Assert.DoesNotContain(
                TestContent.RelicBuffSkillId, pack.GetCharacter(TestContent.HeroCharacterId).SkillIds);
        }

        // ------------------------------------------------------------------
        // Rewards
        // ------------------------------------------------------------------

        [Fact]
        public void Clearing_the_boss_grants_the_guaranteed_clear_reward_and_ends_the_run()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_linear_win", 2024, 0, TestContent.HeroPlacement());

            ExpeditionNodeOutcome last = null;
            while (run.AvailableChoices().Count > 0)
            {
                last = run.Choose(pack, 0);
            }

            Assert.Equal(RunStatus.Cleared, run.State.Status);
            Assert.True(last.RunEnded);
            Assert.Equal(RunStatus.Cleared, last.RunStatusAfter);

            Assert.Single(run.State.BankedMaterials);
            Assert.Equal("mat_test_ore", run.State.BankedMaterials[0].MaterialId);
            Assert.Equal(5, run.State.BankedMaterials[0].Amount);

            // Three pre-boss battle nodes, each paying the common table's guaranteed 77 gold.
            Assert.Equal(77 * 3, run.State.BankedGold);
        }

        [Fact]
        public void Reward_rolls_are_deterministic_from_the_run_seed()
        {
            ContentPack pack = TestContent.Load();

            int RollGold(ulong seed)
            {
                ExpeditionRun run = ExpeditionRun.Start(
                    pack, "stg_test_reward_range", seed, 0, TestContent.HeroPlacement());
                ExpeditionNodeOutcome outcome = run.Choose(pack, 0);
                return outcome.Rewards[0].Amount;
            }

            int first = RollGold(90210);
            int second = RollGold(90210);

            Assert.Equal(first, second);
            Assert.InRange(first, 1, 1000);
        }

        // ------------------------------------------------------------------
        // Losing
        // ------------------------------------------------------------------

        [Fact]
        public void A_lost_battle_ends_the_run_but_keeps_rewards_banked_so_far()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_lose", 1, 0, TestContent.HeroPlacement());

            run.Choose(pack, 0); // treasure: guaranteed 77 gold
            Assert.Equal(77, run.State.BankedGold);

            ExpeditionNodeOutcome lossOutcome = run.Choose(pack, 0); // guaranteed loss
            Assert.Equal(BattleOutcome.DefenderVictory, lossOutcome.BattleResult.Outcome);
            Assert.True(lossOutcome.RunEnded);
            Assert.Equal(RunStatus.Failed, run.State.Status);
            Assert.Equal(77, run.State.BankedGold);

            Assert.Empty(run.AvailableChoices());
            Assert.Throws<ExpeditionException>(() => run.Choose(pack, 0));
        }

        [Fact]
        public void A_stage_can_choose_to_wipe_banked_rewards_on_defeat_instead()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_lose_no_keep", 1, 0, TestContent.HeroPlacement());

            run.Choose(pack, 0);
            Assert.Equal(77, run.State.BankedGold);

            run.Choose(pack, 0);
            Assert.Equal(0, run.State.BankedGold);
            Assert.Equal(RunStatus.Failed, run.State.Status);
        }

        // ------------------------------------------------------------------
        // Serialization and versioning
        // ------------------------------------------------------------------

        [Fact]
        public void A_run_resumes_from_its_serialized_state_with_an_identical_outcome()
        {
            ContentPack pack = TestContent.Load();

            ExpeditionRun baseline = ExpeditionRun.Start(
                pack, "stg_test_linear_win", 4242, 0, TestContent.HeroPlacement());
            while (baseline.AvailableChoices().Count > 0)
            {
                baseline.Choose(pack, 0);
            }

            ExpeditionRun partial = ExpeditionRun.Start(
                pack, "stg_test_linear_win", 4242, 0, TestContent.HeroPlacement());
            partial.Choose(pack, 0);
            partial.Choose(pack, 0);
            string json = partial.Serialize();

            ExpeditionRun resumed = ExpeditionRun.Resume(pack, json);
            while (resumed.AvailableChoices().Count > 0)
            {
                resumed.Choose(pack, 0);
            }

            Assert.Equal(baseline.State.ComputeChecksum(), resumed.State.ComputeChecksum());
            Assert.Equal(baseline.State.Status, resumed.State.Status);
            Assert.Equal(baseline.State.BankedGold, resumed.State.BankedGold);
            Assert.Equal(baseline.State.BankedMaterials.Count, resumed.State.BankedMaterials.Count);
            Assert.Equal(baseline.State.Team[0].CurrentHp, resumed.State.Team[0].CurrentHp);
        }

        [Fact]
        public void Resume_refuses_a_different_content_version()
        {
            ContentPack packV1 = TestContent.Load("test.1");
            ContentPack packV2 = TestContent.Load("test.2");

            ExpeditionRun run = ExpeditionRun.Start(packV1, "stg_test_linear_win", 1, 0, TestContent.HeroPlacement());
            string json = run.Serialize();

            ExpeditionException error = Assert.Throws<ExpeditionException>(() => ExpeditionRun.Resume(packV2, json));
            Assert.Contains("test.1", error.Message);
        }

        [Fact]
        public void Resume_refuses_a_mismatched_rules_version()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_linear_win", 1, 0, TestContent.HeroPlacement());

            string json = Regex.Replace(
                run.Serialize(), "\"rulesVersion\"\\s*:\\s*\"[^\"]*\"", "\"rulesVersion\": \"0.0.0\"");

            ExpeditionException error = Assert.Throws<ExpeditionException>(() => ExpeditionRun.Resume(pack, json));
            Assert.Contains("0.0.0", error.Message);
        }

        [Fact]
        public void Choose_refuses_content_other_than_what_the_run_began_under_even_without_a_resume()
        {
            ContentPack packV1 = TestContent.Load("test.1");
            ContentPack packV2 = TestContent.Load("test.2");

            ExpeditionRun run = ExpeditionRun.Start(packV1, "stg_test_linear_win", 1, 0, TestContent.HeroPlacement());

            Assert.Throws<ExpeditionException>(() => run.Choose(packV2, 0));
        }

        // ------------------------------------------------------------------
        // Input validation
        // ------------------------------------------------------------------

        [Fact]
        public void Choosing_an_index_outside_the_available_choices_is_rejected()
        {
            ContentPack pack = TestContent.Load();
            ExpeditionRun run = ExpeditionRun.Start(pack, "stg_test_linear_win", 1, 0, TestContent.HeroPlacement());

            Assert.Throws<ExpeditionException>(() => run.Choose(pack, 99));
            Assert.Throws<ExpeditionException>(() => run.Choose(pack, -1));
        }

        [Fact]
        public void Starting_a_run_without_a_leader_present_is_rejected()
        {
            ContentPack pack = TestContent.Load();

            ExpeditionException error = Assert.Throws<ExpeditionException>(() =>
                ExpeditionRun.Start(pack, "stg_test_linear_win", 1, 4, TestContent.HeroPlacement()));

            Assert.Contains("Leader slot", error.Message);
        }

        [Fact]
        public void Starting_a_run_with_a_duplicate_slot_is_rejected()
        {
            ContentPack pack = TestContent.Load();
            var placements = new List<KeyValuePair<int, OwnedCharacter>>
            {
                new KeyValuePair<int, OwnedCharacter>(0, TestContent.HeroPlacement()[0].Value),
                new KeyValuePair<int, OwnedCharacter>(0, TestContent.HeroPlacement()[0].Value)
            };

            ExpeditionException error = Assert.Throws<ExpeditionException>(() =>
                ExpeditionRun.Start(pack, "stg_test_linear_win", 1, 0, placements));

            Assert.Contains("used twice", error.Message);
        }
    }
}
