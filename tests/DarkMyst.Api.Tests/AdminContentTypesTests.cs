using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Tests.Infra;
using DarkMyst.Combat.Model;
using DarkMyst.Content;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// The same edit → validate → publish → rollback proof <see cref="AdminContentTests"/> runs
    /// for characters, run for the three content types added alongside it
    /// (docs/11-admin-spec.md): skills, enemies and encounters. Every assertion here reads the
    /// isolated <c>content/</c> copy off disk (<see cref="AdminApiTestFixture.ContentRoot"/>), not
    /// just the API's response, for the same reason <see cref="AdminContentTests"/> does.
    /// </summary>
    public sealed class AdminContentTypesTests : IClassFixture<AdminApiTestFixture>
    {
        private readonly AdminApiTestFixture _fixture;

        public AdminContentTypesTests(AdminApiTestFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<(string Token, AdminContentCurrentDto Current)> GetCurrentAsAdminAsync()
        {
            (_, string adminToken) = await _fixture.Client.CreateAdminAsync();
            var response = await _fixture.Client.AdminGet("/admin/content/current", adminToken);
            response.EnsureSuccessStatusCode();
            var current = await response.Content.ReadFromJsonAsync<AdminContentCurrentDto>(Json.Options);
            return (adminToken, current);
        }

        private string ManifestPath => Path.Combine(_fixture.ContentRoot, "manifest.json");

        private string ReadManifestVersion()
        {
            return JObject.Parse(File.ReadAllText(ManifestPath))["contentVersion"]!.Value<string>();
        }

        // ------------------------------------------------------------------
        // Skills
        // ------------------------------------------------------------------

        [Fact]
        public async Task Skill_edit_publishes_to_disk_and_rolls_back()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string originalVersion = before.Version;
            List<SkillDefinition> skills = before.Skills;
            SkillDefinition edited = skills.First(s => s.Id == "skl_basic_strike");
            string originalName = edited.Name;
            edited.Name = originalName + " (buffed)";

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "skill-publish-" + System.Guid.NewGuid(),
                body: new { skills, notes = "bump strike name" });

            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
            var published = await publishResponse.Content.ReadFromJsonAsync<AdminPublishResultDto>(Json.Options);
            Assert.NotEqual(originalVersion, published.Version);

            string skillsPath = Path.Combine(_fixture.ContentRoot, "skills.json");
            var onDisk = JObject.Parse(File.ReadAllText(skillsPath));
            string nameOnDisk = onDisk["skills"]!.First(s => s["id"]!.Value<string>() == "skl_basic_strike")["name"]!.Value<string>();
            Assert.Equal(originalName + " (buffed)", nameOnDisk);
            Assert.Equal(published.Version, ReadManifestVersion());

            var rollbackResponse = await _fixture.Client.AdminPost(
                "/admin/content/rollback", token, idempotencyKey: "skill-rollback-" + System.Guid.NewGuid(),
                body: new { targetVersion = originalVersion, notes = "undo" });
            Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);

            var revertedOnDisk = JObject.Parse(File.ReadAllText(skillsPath));
            string nameAfterRollback = revertedOnDisk["skills"]!.First(s => s["id"]!.Value<string>() == "skl_basic_strike")["name"]!.Value<string>();
            Assert.Equal(originalName, nameAfterRollback);
            Assert.Equal(originalVersion, ReadManifestVersion());
        }

        [Fact]
        public async Task Publish_refuses_a_skill_list_missing_a_skill_a_character_still_references()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            List<SkillDefinition> skills = before.Skills;
            skills.RemoveAll(s => s.Id == "skl_basic_strike");

            var validateResponse = await _fixture.Client.AdminPost(
                "/admin/content/validate", token, body: new { skills, notes = (string)null });
            var validated = await validateResponse.Content.ReadFromJsonAsync<AdminValidateResultDto>(Json.Options);
            Assert.False(validated.Valid);
            Assert.Contains(validated.Errors, e => e.Contains("unknown skill"));

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "skill-invalid-" + System.Guid.NewGuid(),
                body: new { skills, notes = (string)null });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, publishResponse.StatusCode);

            // content/ was never touched: still whatever version this fixture started at, and the
            // skill is still on disk.
            var onDisk = JObject.Parse(File.ReadAllText(Path.Combine(_fixture.ContentRoot, "skills.json")));
            Assert.Contains(onDisk["skills"]!, s => s["id"]!.Value<string>() == "skl_basic_strike");
        }

        // ------------------------------------------------------------------
        // Enemies
        // ------------------------------------------------------------------

        [Fact]
        public async Task Enemy_edit_publishes_to_disk_and_rolls_back()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string originalVersion = before.Version;
            List<EnemyData> enemies = before.Enemies;
            EnemyData edited = enemies.First(e => e.Id == "enm_gloom_hound");
            int originalAttack = edited.Stats.Attack;
            edited.Stats.Attack = originalAttack + 40;

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "enemy-publish-" + System.Guid.NewGuid(),
                body: new { enemies, notes = "buff gloom hound" });
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
            var published = await publishResponse.Content.ReadFromJsonAsync<AdminPublishResultDto>(Json.Options);

            string enemiesPath = Path.Combine(_fixture.ContentRoot, "enemies.json");
            var onDisk = JObject.Parse(File.ReadAllText(enemiesPath));
            int attackOnDisk = onDisk["enemies"]!.First(e => e["id"]!.Value<string>() == "enm_gloom_hound")["stats"]!["attack"]!.Value<int>();
            Assert.Equal(originalAttack + 40, attackOnDisk);

            var rollbackResponse = await _fixture.Client.AdminPost(
                "/admin/content/rollback", token, idempotencyKey: "enemy-rollback-" + System.Guid.NewGuid(),
                body: new { targetVersion = originalVersion, notes = "undo" });
            Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);

            var revertedOnDisk = JObject.Parse(File.ReadAllText(enemiesPath));
            int attackAfterRollback = revertedOnDisk["enemies"]!.First(e => e["id"]!.Value<string>() == "enm_gloom_hound")["stats"]!["attack"]!.Value<int>();
            Assert.Equal(originalAttack, attackAfterRollback);
            Assert.NotEqual(originalVersion, published.Version);
        }

        [Fact]
        public async Task Publish_refuses_an_enemy_left_with_no_guaranteed_turn_action()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            List<EnemyData> enemies = before.Enemies;
            enemies.First(e => e.Id == "enm_gloom_hound").SkillIds.Clear();

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "enemy-invalid-" + System.Guid.NewGuid(),
                body: new { enemies, notes = (string)null });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, publishResponse.StatusCode);
            var problem = await publishResponse.Content.ReadFromJsonAsync<ContentInvalidDto>(Json.Options);
            Assert.Contains(problem.Problems, p => p.Contains("no guaranteed OnAction skill"));
        }

        // ------------------------------------------------------------------
        // Encounters
        // ------------------------------------------------------------------

        [Fact]
        public async Task Encounter_edit_publishes_to_disk_and_rolls_back()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string originalVersion = before.Version;
            List<EncounterData> encounters = before.Encounters;
            EncounterData edited = encounters.First(e => e.Id == "enc_tutorial_hounds");
            string originalName = edited.Name;
            edited.Name = originalName + " (revised)";

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "encounter-publish-" + System.Guid.NewGuid(),
                body: new { encounters, notes = "rename encounter" });
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
            var published = await publishResponse.Content.ReadFromJsonAsync<AdminPublishResultDto>(Json.Options);

            string encountersPath = Path.Combine(_fixture.ContentRoot, "encounters.json");
            var onDisk = JObject.Parse(File.ReadAllText(encountersPath));
            string nameOnDisk = onDisk["encounters"]!.First(e => e["id"]!.Value<string>() == "enc_tutorial_hounds")["name"]!.Value<string>();
            Assert.Equal(originalName + " (revised)", nameOnDisk);

            var rollbackResponse = await _fixture.Client.AdminPost(
                "/admin/content/rollback", token, idempotencyKey: "encounter-rollback-" + System.Guid.NewGuid(),
                body: new { targetVersion = originalVersion, notes = "undo" });
            Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);

            var revertedOnDisk = JObject.Parse(File.ReadAllText(encountersPath));
            string nameAfterRollback = revertedOnDisk["encounters"]!.First(e => e["id"]!.Value<string>() == "enc_tutorial_hounds")["name"]!.Value<string>();
            Assert.Equal(originalName, nameAfterRollback);
            Assert.NotEqual(originalVersion, published.Version);
        }

        [Fact]
        public async Task Publish_refuses_an_encounter_left_with_no_units()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            List<EncounterData> encounters = before.Encounters;
            encounters.First(e => e.Id == "enc_tutorial_hounds").Units.Clear();

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "encounter-invalid-" + System.Guid.NewGuid(),
                body: new { encounters, notes = (string)null });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, publishResponse.StatusCode);
            var problem = await publishResponse.Content.ReadFromJsonAsync<ContentInvalidDto>(Json.Options);
            Assert.Contains(problem.Problems, p => p.Contains("has no units"));
        }

        [Fact]
        public async Task Editing_one_type_leaves_the_others_file_untouched_on_disk()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string charactersPathBefore = File.ReadAllText(Path.Combine(_fixture.ContentRoot, "characters.json"));

            List<SkillDefinition> skills = before.Skills;
            skills.First(s => s.Id == "skl_basic_strike").Name = "Renamed Only Here";

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "skill-only-" + System.Guid.NewGuid(),
                body: new { skills, notes = (string)null });
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

            string charactersPathAfter = File.ReadAllText(Path.Combine(_fixture.ContentRoot, "characters.json"));
            Assert.Equal(charactersPathBefore, charactersPathAfter);
        }

        private sealed record AdminContentCurrentDto(
            string Version, string RulesVersion, List<CharacterData> Characters, List<SkillDefinition> Skills,
            List<EnemyData> Enemies, List<EncounterData> Encounters, List<string> RollbackableVersions);

        private sealed record AdminValidateResultDto(bool Valid, List<string> Errors);

        private sealed record AdminPublishResultDto(
            string Version, string PreviousVersion, int CharacterCount, int SkillCount, int EnemyCount,
            int EncounterCount, System.DateTimeOffset PublishedAt);

        private sealed record ContentInvalidDto(string Error, List<string> Problems);
    }
}
