using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DarkMyst.Api.Tests.Infra;
using DarkMyst.Content;
using Xunit;

namespace DarkMyst.Api.Tests
{
    /// <summary>
    /// The core mechanics acceptance criterion: edit → validate → publish → (rollback), all
    /// through the real <c>content/</c> directory (an isolated copy per
    /// <see cref="AdminApiTestFixture"/>, never the repo's own) and the real
    /// <c>ContentPack.Validate()</c> — no mock, no parallel content model
    /// (server/DarkMyst.Api/Admin/AdminContentService.cs, docs/11-admin-spec.md).
    /// </summary>
    public sealed class AdminContentTests : IClassFixture<AdminApiTestFixture>
    {
        private readonly AdminApiTestFixture _fixture;

        public AdminContentTests(AdminApiTestFixture fixture)
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

        [Fact]
        public async Task Validate_accepts_a_harmless_stat_edit()
        {
            (string token, AdminContentCurrentDto current) = await GetCurrentAsAdminAsync();
            List<CharacterData> characters = current.Characters;
            characters[0].BaseStats.Attack += 5;

            var response = await _fixture.Client.AdminPost(
                "/admin/content/validate", token, body: new { characters, notes = (string)null });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<AdminValidateResultDto>(Json.Options);
            Assert.True(result.Valid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public async Task Validate_refuses_a_character_left_with_no_skills()
        {
            (string token, AdminContentCurrentDto current) = await GetCurrentAsAdminAsync();
            List<CharacterData> characters = current.Characters;
            characters[0].SkillIds.Clear();

            var response = await _fixture.Client.AdminPost(
                "/admin/content/validate", token, body: new { characters, notes = (string)null });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<AdminValidateResultDto>(Json.Options);
            Assert.False(result.Valid);
            Assert.Contains(result.Errors, e => e.Contains("has no skills"));
        }

        [Fact]
        public async Task Publish_writes_the_new_version_to_content_and_the_registry_and_records_an_audit_row()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string originalVersion = before.Version;
            CharacterData edited = before.Characters.First(c => c.Id == "chr_ashen_knight_i");
            int originalAttack = edited.BaseStats.Attack;
            edited.BaseStats.Attack = originalAttack + 77;

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "publish-" + System.Guid.NewGuid(),
                body: new { characters = before.Characters, notes = "bump ashen knight attack" });

            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
            var published = await publishResponse.Content.ReadFromJsonAsync<AdminPublishResultDto>(Json.Options);
            Assert.Equal(originalVersion, published.PreviousVersion);
            Assert.NotEqual(originalVersion, published.Version);

            // 1. The live registry now serves the new version.
            var afterResponse = await _fixture.Client.AdminGet("/admin/content/current", token);
            var after = await afterResponse.Content.ReadFromJsonAsync<AdminContentCurrentDto>(Json.Options);
            Assert.Equal(published.Version, after.Version);
            Assert.Equal(originalAttack + 77, after.Characters.First(c => c.Id == "chr_ashen_knight_i").BaseStats.Attack);

            // 2. content/ itself changed on disk, not just this process's memory — re-load it
            // exactly the way a server restart (or the sim runner, or Unity's build step) would.
            ContentPack reloaded = ContentPack.LoadFromDirectory(_fixture.ContentRoot);
            Assert.Equal(published.Version, reloaded.Version);
            Assert.Equal(originalAttack + 77, reloaded.GetCharacter("chr_ashen_knight_i").BaseStats.Attack);

            // 3. The audit trail names who published what and when.
            var versionsResponse = await _fixture.Client.AdminGet("/admin/content/versions", token);
            var versions = await versionsResponse.Content.ReadFromJsonAsync<List<AdminVersionEntryDto>>(Json.Options);
            AdminVersionEntryDto entry = versions.First(v => v.Version == published.Version);
            Assert.Equal("Publish", entry.Kind);
            Assert.Equal(originalVersion, entry.PreviousVersion);
            Assert.NotNull(entry.PublishedByAdminId);
        }

        [Fact]
        public async Task Publish_replays_on_a_repeated_Idempotency_Key_instead_of_bumping_the_version_twice()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string key = "publish-replay-" + System.Guid.NewGuid();
            object body = new { characters = before.Characters, notes = "no-op republish" };

            var first = await _fixture.Client.AdminPost("/admin/content/publish", token, idempotencyKey: key, body: body);
            var second = await _fixture.Client.AdminPost("/admin/content/publish", token, idempotencyKey: key, body: body);

            string firstBody = await first.Content.ReadAsStringAsync();
            string secondBody = await second.Content.ReadAsStringAsync();
            Assert.Equal(firstBody, secondBody);
        }

        [Fact]
        public async Task Publish_is_refused_for_invalid_content_and_leaves_content_unchanged()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string originalVersion = before.Version;
            before.Characters[0].SkillIds.Clear();

            var response = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "publish-invalid-" + System.Guid.NewGuid(),
                body: new { characters = before.Characters, notes = (string)null });

            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ContentInvalidDto>(Json.Options);
            Assert.Equal("content_invalid", problem.Error);
            Assert.NotEmpty(problem.Problems);

            var afterResponse = await _fixture.Client.AdminGet("/admin/content/current", token);
            var after = await afterResponse.Content.ReadFromJsonAsync<AdminContentCurrentDto>(Json.Options);
            Assert.Equal(originalVersion, after.Version);

            string manifestOnDisk = await File.ReadAllTextAsync(Path.Combine(_fixture.ContentRoot, "manifest.json"));
            Assert.Contains("\"contentVersion\": \"" + originalVersion + "\"", manifestOnDisk);
        }

        [Fact]
        public async Task Rollback_restores_the_previous_version_on_disk_and_in_the_registry()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string originalVersion = before.Version;
            CharacterData edited = before.Characters.First(c => c.Id == "chr_grave_warden_i");
            int originalDefense = edited.BaseStats.Defense;
            edited.BaseStats.Defense = originalDefense + 42;

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "publish-for-rollback-" + System.Guid.NewGuid(),
                body: new { characters = before.Characters, notes = "temporary defense bump" });
            var published = await publishResponse.Content.ReadFromJsonAsync<AdminPublishResultDto>(Json.Options);

            // The version we just published *from* is rollback-eligible even though nothing ever
            // published *it* (so it has no row of its own in the audit log) — publish snapshots the
            // outgoing version before overwriting it (AdminContentService.PublishAsync).
            var midResponse = await _fixture.Client.AdminGet("/admin/content/current", token);
            var mid = await midResponse.Content.ReadFromJsonAsync<AdminContentCurrentDto>(Json.Options);
            Assert.Contains(originalVersion, mid.RollbackableVersions);

            var rollbackResponse = await _fixture.Client.AdminPost(
                "/admin/content/rollback", token, idempotencyKey: "rollback-" + System.Guid.NewGuid(),
                body: new { targetVersion = originalVersion, notes = "revert the defense bump" });

            Assert.Equal(HttpStatusCode.OK, rollbackResponse.StatusCode);
            var rolledBack = await rollbackResponse.Content.ReadFromJsonAsync<AdminRollbackResultDto>(Json.Options);
            Assert.Equal(originalVersion, rolledBack.Version);
            Assert.Equal(published.Version, rolledBack.PreviousVersion);

            var afterResponse = await _fixture.Client.AdminGet("/admin/content/current", token);
            var after = await afterResponse.Content.ReadFromJsonAsync<AdminContentCurrentDto>(Json.Options);
            Assert.Equal(originalVersion, after.Version);
            Assert.Equal(originalDefense, after.Characters.First(c => c.Id == "chr_grave_warden_i").BaseStats.Defense);

            ContentPack reloaded = ContentPack.LoadFromDirectory(_fixture.ContentRoot);
            Assert.Equal(originalVersion, reloaded.Version);
            Assert.Equal(originalDefense, reloaded.GetCharacter("chr_grave_warden_i").BaseStats.Defense);
        }

        [Fact]
        public async Task Rollback_to_a_version_with_no_snapshot_is_refused_with_a_clean_409()
        {
            (string token, _) = await GetCurrentAsAdminAsync();

            var response = await _fixture.Client.AdminPost(
                "/admin/content/rollback", token, idempotencyKey: "rollback-unknown-" + System.Guid.NewGuid(),
                body: new { targetVersion = "9.9.9", notes = (string)null });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task Diff_reports_the_field_that_changed_between_two_versions()
        {
            (string token, AdminContentCurrentDto before) = await GetCurrentAsAdminAsync();
            string fromVersion = before.Version;
            CharacterData edited = before.Characters.First(c => c.Id == "chr_tide_oracle_i");
            edited.BaseStats.Speed += 3;

            var publishResponse = await _fixture.Client.AdminPost(
                "/admin/content/publish", token, idempotencyKey: "publish-for-diff-" + System.Guid.NewGuid(),
                body: new { characters = before.Characters, notes = "speed tweak" });
            var published = await publishResponse.Content.ReadFromJsonAsync<AdminPublishResultDto>(Json.Options);

            var diffResponse = await _fixture.Client.AdminGet(
                $"/admin/content/diff?from={fromVersion}&to={published.Version}", token);

            Assert.Equal(HttpStatusCode.OK, diffResponse.StatusCode);
            var diff = await diffResponse.Content.ReadFromJsonAsync<AdminContentDiffDto>(Json.Options);
            AdminCharacterDiffDto changed = diff.ChangedCharacters.First(c => c.CharacterId == "chr_tide_oracle_i");
            Assert.Contains(changed.Fields, f => f.Path == "baseStats.speed");
        }

        // Mirrors the shape of server/DarkMyst.Api/Admin/AdminDtos.cs closely enough to deserialize
        // responses; kept local to the test project rather than shared, the same way other test
        // classes in this project declare their own small response records.
        private sealed record AdminContentCurrentDto(string Version, string RulesVersion, List<CharacterData> Characters, List<string> RollbackableVersions);
        private sealed record AdminValidateResultDto(bool Valid, List<string> Errors);
        private sealed record AdminPublishResultDto(string Version, string PreviousVersion, int CharacterCount, System.DateTimeOffset PublishedAt);
        private sealed record AdminRollbackResultDto(string Version, string PreviousVersion, System.DateTimeOffset PublishedAt);
        private sealed record AdminVersionEntryDto(string Version, string PreviousVersion, string Kind, string PublishedByAdminId, string Notes, System.DateTimeOffset PublishedAt);
        private sealed record ContentInvalidDto(string Error, List<string> Problems);
        private sealed record AdminFieldChangeDto(string Path, string Before, string After);
        private sealed record AdminCharacterDiffDto(string CharacterId, List<AdminFieldChangeDto> Fields);
        private sealed record AdminContentDiffDto(string From, string To, List<string> AddedCharacterIds, List<string> RemovedCharacterIds, List<AdminCharacterDiffDto> ChangedCharacters);
    }
}
