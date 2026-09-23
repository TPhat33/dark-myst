---
name: checkpoint
description: Write and push a handoff so work on Dark Myst continues in a fresh session without losing state. Use it whenever the context-budget hook warns, the session is long or slow, before a risky change worth a restore point, when work must pause with something half-done, and always before ending a session with anything in flight. Trigger on "checkpoint", "handoff", "start a new session", "ขึ้น session ใหม่", "สรุปสถานะ", "จดสถานะไว้ก่อน", or any ask to pause and pick this up later.
---

# Checkpoint

## Why a handoff is not a summary

The container this session runs in is reclaimed when the session ends. Anything
not committed and pushed is gone — not stale, gone. So the deliverable of a
checkpoint is a **pushed commit**, not a nicely written message in the chat.

The other half is subtler. When a session's context fills, it is auto-summarized:
the shape of the work survives, the specifics do not. In this project the
specifics are the value. Almost every claim on record — "175 tests green", "boss
lv12 83.3% over 300 seeds", "Umbral 31.3% of enemy units" — was measured by
running something, and the standing rule here is that a claim is verified or it
is not made. A summary that turns 83.3% into "the boss curve is tuned" has
destroyed the only part worth keeping, and the next session will repeat work to
recover it or, worse, will not notice it needs to.

A checkpoint is therefore an act of measuring and writing down, not of recalling.

## Step 1 — measure the state, don't remember it

Run these and use the real output. If the session already ran one of them and
nothing has changed since, reuse that output rather than burning context; if
anything at all has been edited, re-run it.

```bash
git branch --show-current && git rev-parse --short HEAD
git status --short                                    # must end empty
git log --oneline -8
git log --oneline origin/$(git branch --show-current)..HEAD   # unpushed work

dotnet test                                           # needs PostgreSQL running
dotnet run --project tools/DarkMyst.SimRunner -- validate
dotnet run --project tools/DarkMyst.SimRunner -- roster
jq -r '.contentVersion, .rulesVersion' content/manifest.json

cd admin && npm test                                  # only if admin/ was touched
```

If a command fails, that failure **is** the state — write it down with its real
error. A handoff that quietly omits a red test is worse than no handoff, because
the next session starts by trusting it.

## Step 2 — write HANDOFF.md

Write to `HANDOFF.md` at the repo root, replacing whatever is there. One file,
always current: old checkpoints stay readable in git history, so keeping a folder
of them only adds files that are wrong.

Write it for a Claude that has read nothing yet. Keep the user's own words in
Thai where they stated intent — a paraphrase of "อยากให้เกมมันค่อยๆมีตัวละครเพิ่มขึ้นเรื่อยๆ"
loses exactly the nuance that makes it a design constraint.

```markdown
# Handoff — <YYYY-MM-DD>

## Where the work is
Branch `claude/2d-rpg-team-battle-farm-0dqcx2` at `<sha>`, pushed / N commits unpushed.
Working tree clean | dirty (list the files and say why they are uncommitted).

## Verified just now
- dotnet test: <N> green (<breakdown by project>) | <what failed, with the error>
- simrunner validate: <result>
- content <contentVersion> / rules <rulesVersion>
- <any balance numbers re-measured, with the seed count that produced them>

## What just landed
<2–5 lines. What changed and what it was for — not a commit list, git has that.>

## In flight
<Subagents running and what they were told; work half-done and where it stops.
Nothing in flight is a fine answer — say so explicitly rather than leaving it out.>

## Open decisions for the user
<Each with the options and the trade-off, so the next session can put the choice
to them without re-deriving it. Name any that have been raised and not answered.>

## Standing rules in force
<Only the ones not evident from the repo — see below.>

## Next step
<The one thing to do first, concretely enough to start on.>
```

## Step 3 — commit and push

```bash
git add -A && git commit && git push -u origin claude/2d-rpg-team-battle-farm-0dqcx2
```

Push to that branch only. If the push fails on a network error, retry with
backoff (2s, 4s, 8s, 16s) — an unpushed checkpoint is not a checkpoint.

## Step 4 — hand over

Tell the user, in the language they are writing in, that the checkpoint is pushed,
and give them the one line to open the next session with: *read HANDOFF.md on
branch `claude/2d-rpg-team-battle-farm-0dqcx2`, then continue*. Mention anything
they must decide before work can resume.

## What earns a place in the handoff

The test is simple: **could the next session recover this by reading the repo?**
If yes, leave it out. Re-describing the architecture, the folder layout or the
combat formulas spends the fresh session's context on what `README.md`,
`docs/00-overview.md` and `docs/02-combat-spec.md` say better, and every copy
is one more thing that can go stale and mislead.

What cannot be recovered, and so must be written down:

- **Which numbers were measured, by what command, at what sample size.** A number
  without its provenance cannot be trusted or reproduced later.
- **Why a decision went the way it did** — including the options rejected. The
  reasoning is what stops the next session relitigating it or quietly undoing it.
- **The user's intent in their own words.** Design intent is not in the code.
- **Dead ends.** What was tried and did not work, so nobody pays for it twice.
- **Environment facts that cost effort to establish** — that Docker is
  unavailable so Testcontainers is out, that PostgreSQL runs locally under a
  specific role, that no Unity Editor exists in this container.
- **Standing constraints an agent would otherwise break**: the three rules
  libraries stay netstandard2.1 with no Unity reference, no floating point and
  `DeterministicRandom` only; a change to any of them must be disclosed, never
  slipped in; admin endpoints keep their authorization gate structurally
  separate from the player token; publishing content is refused when
  `ContentPack.Validate()` fails, with no override flag; content edits bump
  `contentVersion`; no secrets in committed source.

## When the work isn't at a clean stopping point

Checkpoint anyway — a handoff describing a half-finished change is far better
than losing it. Commit the partial work on the branch, and say plainly in **In
flight** what state it is in and what was about to happen next. The one thing to
avoid is leaving it uncommitted in a container that is about to disappear.
