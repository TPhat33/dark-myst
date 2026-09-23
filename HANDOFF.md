# Handoff — 2026-09-23

Read this, then `README.md` and `docs/00-overview.md`. Everything the repo already
explains is deliberately left out here; this file is only what dies with a session.

## Where the work is

Branch `claude/2d-rpg-team-battle-farm-0dqcx2` at `37721ce`, pushed, working tree clean.
No pull request has been opened and none should be without the user asking.

## Verified just now

Measured by running the commands, not recalled:

- `dotnet test` — **175 green, 0 failed**: Combat 55, Content 51, Expedition 20, Sim 6,
  Api 43. (The `DbUpdateException` lines in the Api log are tests that deliberately
  exercise the idempotency conflict path, not failures.)
- `simrunner validate` — Content 0.4.0 valid for rules 1.0.0: 19 characters, 8 enemies,
  4 encounters, 40 skills, 1 expedition stage, 3 reward tables, 2 events.
- `simrunner roster` — 14 playable stage-I lines. 16 enemy units: Neutral 31.3%,
  Umbral 31.3%, Radiant 18.8%, Ember/Verdant/Tide 6.3% each. Rarity 4 = 2 lines across
  2 roles (Controller, Mender), most-concentrated role 50.0% of the tier. All three
  composition guards green.
- `content/manifest.json` — contentVersion **0.4.0**, rulesVersion **1.0.0**.
- `admin/` npm tests not run — `admin/` was not touched this session.

Difficulty and win-rate curves are **not** repeated here: they live in
`docs/07-testing-plan.md`, re-measured at 300+ seeds against content 0.4.0 in commit
`a15ff7e`. Trust that file, and re-measure rather than reasoning about them.

## What just landed

Two rounds since the expedition work:

1. **Roster balance made enforceable** (`9c60660`, `bf55bd6`, `00e9024`, `a15ff7e`,
   `29e32f2`). The user asked for a *note* of how many characters each affinity has.
   A hand-written note goes stale in two content updates and then actively misleads,
   so it became `simrunner roster` (generated from live content) plus three CI guards:
   no enemy affinity above 40% of units, every affinity has ≥1 enemy unit and ≥1
   playable stage-I line, and the top pullable rarity tier is never single-role.
   Writing the guards exposed two real content bugs, both fixed in 0.4.0: enemy
   affinities were Umbral 8/15 with **Tide 0 and Radiant 0** (which made Radiant
   accidentally the strongest affinity), and **both rarity-4 lines were Menders** —
   the measured most-decisive slot, i.e. an unintended pay-to-win shape about to have
   a gacha built on top of it.
2. **The summon spec** (`2820964`, `948738e`) — `docs/12-summon-spec.md`, wired into
   both indexes.
3. **This session's handover tooling** (`37721ce`) — the `/checkpoint` skill and the
   context-budget hook that produced this file.

## In flight

**Nothing.** No subagents are running, no work is half-applied, nothing is uncommitted.
A fresh session starts clean.

## Decisions the user made on 2026-09-23

1. **Sell pulls** — docs/04 rule amended to "gems buy chance and time, never guaranteed
   advantage" (conditions in docs/04 and docs/12). Revenue reason, the user's call.
2. **R5 comes later** — the row stays as a placeholder; R5 rolls fold into R4.
3. **Reference Genshin/Ragnarok** — done as a measured comparison
   (`simrunner summon --compare`, `--rules genshin-like`), not a copy. docs/12 §"เทียบกับโครงแบบ
   Genshin" lists three proposals that are **not yet applied**: separate R5 pity when R5 lands,
   a featured banner once staged release starts, revisit Echo shard rates at rate-lock.
   Asked the user whether to apply proposal 1 now; unanswered.
4. **Telemetry approved and built** — `telemetry_events` + admin `/admin/telemetry/{events,lines}`
   (docs/10). Win rate counts expedition battles only (the /battle/run sandbox is unlimited).

Still open: which ~4 lines to hold back for staged release (docs/12 §"แผนปล่อยตัวละคร").

## Design intent, in the user's own words

Paraphrase loses the constraint, so these are verbatim:

- Gradual roster growth is the retention plan, not a nice-to-have:
  *"มีตัวละครทีเดียวครบตั้งแต่ day 1 แล้วจะมีอะไรไปดึงความสนใจให้ผู้เล่นอยู่กับเรานานๆ"*
- Legendary and evolve are separate concerns:
  *"เรื่อง legendary กับ evolve ผมมองแยกกัน การเปิดได้ legendary เลย มันก็ได้ในเรื่องของตัวละคร
  แต่เรื่องของ stat ของตัวละครอาจจะสู้การไล่ evolve ตั้งแต่ขั้นแรกไม่ได้"*
  This is already mechanically enforced: `maxLevelByStage = [20, 40, 60]` ties the level
  cap to evolve stage, not rarity, so a freshly pulled stage-I legendary caps at level 20
  (~3,100 HP) against ~10,600 for an evolved stage-III line.
- Telemetry exists to find over- and under-powered characters for later tuning, not to
  produce dashboards.

## How the user works with this repo

Stated repeatedly and still in force: **Sonnet 5 subagents do the implementation, Claude
directs them and personally reviews and verifies before pushing**
(*"Spawn sonnet 5 max effort ไปทำต่อคุณคอยสั่งงาน และตรวจภาพรวม"*,
*"ให้มัน hook กลับมาบอก ไม่ต้องไปรอมันตอบ"* — run them in the background, do not block
waiting on them).

Review here means re-running the commands and reading the diff, not trusting the agent's
report. Two concrete reasons it matters: an agent's 15 tests missed that `/debug/grant-*`
bypassed `IdempotencyService` (two grants with one key produced 100007 gold instead of
50007 — found with curl), and an agent silently modified a rules library despite an
explicit instruction to flag any such change (found by diffing; the change itself was
sound). Also tell every agent to **commit each green piece immediately rather than
batching** — two agents have already been cut off mid-stream by Sonnet rate limits.

## Standing rules an agent would otherwise break

- `DarkMyst.Combat`, `DarkMyst.Content` and `DarkMyst.Expedition` stay **netstandard2.1,
  no Unity reference, no floating point, `DeterministicRandom` only**. Never
  `System.Random`, never `string.GetHashCode()` (.NET randomizes it per process). Any
  change to these three must be disclosed prominently, never slipped in.
- Admin endpoints keep an authorization gate **structurally separate** from the player token.
- Publishing content is refused when `ContentPack.Validate()` fails. No override flag.
- Any content edit bumps `contentVersion` (`docs/01` rule 5).
- No secrets or credentials in committed source.
- Push only to `claude/2d-rpg-team-battle-farm-0dqcx2`.

## Environment facts that cost effort to establish

- **PostgreSQL 16 is installed but not started automatically.** Run
  `service postgresql start` before `dotnet test`, or the 43 Api tests fail. Connect over
  **TCP 127.0.0.1**, not the unix socket — peer auth rejects the `darkmyst` role
  (see `server/DarkMyst.Api/appsettings.Development.json`).
- **Docker is unavailable**, so Testcontainers is not an option; the Api tests run against
  that local PostgreSQL.
- .NET SDK 8 was installed via apt — the Microsoft CDN is blocked by the proxy.
- Node 22 and Playwright Chromium are preinstalled (`PLAYWRIGHT_BROWSERS_PATH=/opt/pw-browsers`);
  never run `playwright install`.
- **No Unity Editor exists in this container.** Unity scenes, sprites, UI and device builds
  cannot be done here at all — that is the remaining phase-B exit criterion and needs a
  real machine.
- A shell gotcha that killed this session's own shell twice: `pkill -f "DarkMyst.Api"`
  matches the bash command line itself. Use `pgrep -f "DarkMys[t][.]Api"` in a command that
  does not contain the literal project path.

## Next step

State at hand-off: `dotnet test` **221 green** (Combat 55, Content 51, Expedition 20, Sim 32,
Api 63), verified by re-running, plus a curl smoke test of telemetry against a live API.
Next useful work, none approved yet: the summon server endpoint (pity/spark server-side,
emits `summon_pulled` per docs/10), or applying the Genshin-derived proposals.
