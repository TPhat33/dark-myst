using System;
using System.Collections.Generic;
using System.IO;
using DarkMyst.Content;
using DarkMyst.Sim;
using Xunit;

namespace DarkMyst.Sim.Tests
{
    /// <summary>
    /// Exercises the exact loop the admin tool's sweep button and <c>simrunner sweep</c> both
    /// call (docs/11-admin-spec.md). Loads the real, shipped <c>content/</c> directory, the same
    /// one <c>DarkMyst.Content.Tests</c> uses, so these numbers are the real balance numbers, not
    /// numbers from a hand-built fixture pack.
    /// </summary>
    public sealed class BattleSweepRunnerTests
    {
        private static readonly Lazy<ContentPack> Pack = new Lazy<ContentPack>(
            () => ContentPack.LoadFromDirectory(ContentDirectory));

        private static string ContentDirectory => Path.Combine(FindRepositoryRoot(), "content");

        private static readonly IReadOnlyList<string> DefaultRoster = new List<string>
        {
            "chr_ashen_knight_i",
            "chr_grave_warden_i",
            "chr_ember_adept_i",
            "chr_tide_oracle_i",
            "chr_pale_stalker_i"
        };

        [Fact]
        public void Run_reports_real_win_rate_and_survival_for_the_tutorial_encounter()
        {
            SweepResult result = BattleSweepRunner.Run(Pack.Value, new SweepRequest
            {
                EncounterId = "enc_tutorial_hounds",
                Roster = DefaultRoster,
                Level = 20,
                Seed = 1,
                Repeat = 50
            });

            Assert.Equal("enc_tutorial_hounds", result.EncounterId);
            Assert.Equal(50, result.Battles);
            Assert.InRange(result.Wins, 0, 50);
            Assert.InRange(result.Draws, 0, 50 - result.Wins);
            Assert.True(result.AverageRounds > 0);
            Assert.NotEmpty(result.Survivors);
            foreach (SweepSurvivorEntry survivor in result.Survivors)
            {
                Assert.InRange(survivor.Survived, 0, 50);
            }
        }

        [Fact]
        public void Run_is_deterministic_for_the_same_seed_and_repeat()
        {
            var request = new SweepRequest
            {
                EncounterId = "enc_tutorial_hounds",
                Roster = DefaultRoster,
                Level = 20,
                Seed = 20260920,
                Repeat = 30
            };

            SweepResult a = BattleSweepRunner.Run(Pack.Value, request);
            SweepResult b = BattleSweepRunner.Run(Pack.Value, request);

            Assert.Equal(a.Wins, b.Wins);
            Assert.Equal(a.Draws, b.Draws);
            Assert.Equal(a.AverageRounds, b.AverageRounds);
            Assert.Equal(a.Survivors.Count, b.Survivors.Count);
        }

        [Fact]
        public void Run_rejects_an_unknown_encounter_id()
        {
            Assert.Throws<ContentException>(() => BattleSweepRunner.Run(Pack.Value, new SweepRequest
            {
                EncounterId = "enc_does_not_exist",
                Roster = DefaultRoster,
                Repeat = 5
            }));
        }

        [Fact]
        public void Run_rejects_an_unknown_roster_character_id()
        {
            Assert.Throws<ContentException>(() => BattleSweepRunner.Run(Pack.Value, new SweepRequest
            {
                EncounterId = "enc_tutorial_hounds",
                Roster = new List<string> { "chr_does_not_exist" },
                Repeat = 5
            }));
        }

        [Fact]
        public void Run_rejects_a_non_positive_repeat()
        {
            Assert.Throws<ArgumentException>(() => BattleSweepRunner.Run(Pack.Value, new SweepRequest
            {
                EncounterId = "enc_tutorial_hounds",
                Roster = DefaultRoster,
                Repeat = 0
            }));
        }

        [Fact]
        public void Run_rejects_an_empty_roster()
        {
            Assert.Throws<ArgumentException>(() => BattleSweepRunner.Run(Pack.Value, new SweepRequest
            {
                EncounterId = "enc_tutorial_hounds",
                Roster = new List<string>(),
                Repeat = 5
            }));
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DarkMyst.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "Could not find DarkMyst.sln above " + AppContext.BaseDirectory + ".");
        }
    }
}
