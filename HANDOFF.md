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

## Open decisions for the user

These are raised and unanswered. None of them should be decided unilaterally.

1. **Selling pulls vs. `docs/04`'s own rule** (`docs/12` §"ข้อขัดแย้งกับ 04-economy-spec.md").
   `docs/04` says gems buy *time and convenience, never stats*. Selling pulls sells a
   chance at power, which breaks that. Two options, written out in `docs/12`: amend the
   rule to *"gems buy chance and time, never guaranteed advantage"*, or make summoning
   free-currency-only and sell only farm-time reducers (honest, but a very thin revenue
   model). **This is a business decision, not a technical one** — deliberately not
   decided. Rates cannot be locked until it is.
2. **No pullable R5 exists.** The rate table in `docs/12` has an R5 row, but in content
   0.4.0 rarity 5 exists only as the stage-III Ashen Revenant Lord. Either author the
   first R5 line or drop the row.
3. **Staged character release conflicts with the current roster.** `docs/12` plans
   ~10 lines at launch and 1 more every 3 weeks — but all 14 are already open, so lines
   must be held back *before* launch. Which 4 to hold is undecided.
4. The user approved items 1 and 2 of the summon plan ("เริ่ม ข้อ 1 กับ 2 ได้เลย"); both
   have landed. Items 3+ (`simrunner summon`, character telemetry) have not been started
   and have not been explicitly approved.

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

`simrunner summon` has landed (`9a77c9b`, `3f46ce6`, `ed51ba6`, plus a review fix: each
player now gets its own PCG stream instead of seed `S + playerIndex`, which made runs with
seed S and S+1 share all but one player). `dotnet test` is **192 green** (Sim 23, was 6).
Measured numbers live in `docs/12` §"วิธีวัด" — re-run the command rather than copying them.

What the numbers already say, for the open decisions above: with no R5 in content the R5 roll
folds into R4 (first R4+ ≈ 21 pulls mean, never past 59). Adding one hypothetical R5 line pushes
first-R5 to ≈ 78 mean with ~16% of players still without it at spark, and a single line hits the
300‰ Attune cap only after ~2,400–3,200 pulls for R4/R5 — Attune as specced is a very long tail.

Still not started and not approved: character telemetry (summon plan item 4). The three open
decisions above remain the user's.
