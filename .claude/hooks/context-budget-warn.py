#!/usr/bin/env python3
"""UserPromptSubmit hook: warn once the session's context window passes a budget.

Why this exists
---------------
A long session degrades before it dies. Recall of decisions made 200 turns ago
gets unreliable, and when the window finally fills, the auto-summary keeps the
shape of the work but loses the specifics — exact numbers, what was measured
versus assumed, which agent was mid-flight. In this repo that matters more than
usual: almost every claim on record ("175 tests green", "boss lv12 83.3%") was
measured, and a summary that rounds it off is worse than no claim at all.

So the point of the warning is not the token count. It is to hand over on
purpose, while the session still remembers precisely, by running /checkpoint
and starting fresh — instead of being handed a lossy summary at a random moment.

How the number is obtained
--------------------------
The transcript records per-request `usage`. For the most recent main-loop
assistant message, the prompt that request carried is

    input_tokens + cache_read_input_tokens + cache_creation_input_tokens

which is the whole context as the API saw it, cached or not. That message's
own `output_tokens` are in the next request's prompt too, so they are added:
the result estimates what the *next* request will carry, which is what the
person deciding whether to continue actually cares about.

Two entries must be skipped or the number is wrong rather than approximate:
  * `isSidechain` — subagent turns. This repo spawns them constantly, and a
    subagent's usage reflects its own small context, not the main session's.
    Counting one makes a nearly-full session look empty.
  * model `<synthetic>` — CLI-generated entries with no real request behind them.

A compaction resets the window, and the first assistant message after it
reports the new, smaller prompt — so reading only the latest entry tracks
compaction correctly with no extra bookkeeping.

Failure policy: a hook that breaks the turn is worse than a missing warning,
so every failure path exits 0 silently.
"""

import json
import os
import sys
import tempfile

# Default budget. The point isn't this exact number — it's leaving enough room
# to run /checkpoint (which runs the test suite and the content validators, and
# writes HANDOFF.md) without the window filling mid-handoff.
DEFAULT_WARN_AT = 300_000

# Having been told once, being told again at the same size is noise. Re-warn
# only after the context has grown by this much again, with firmer wording.
RE_WARN_STEP = 50_000


def budget():
    raw = os.environ.get("DARKMYST_CONTEXT_WARN_TOKENS", "")
    try:
        value = int(raw)
        return value if value > 0 else DEFAULT_WARN_AT
    except ValueError:
        return DEFAULT_WARN_AT


def context_tokens(transcript_path):
    """Estimated size of the next request's prompt, or None if unknowable."""
    latest = None
    try:
        with open(transcript_path, encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                try:
                    entry = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if entry.get("type") != "assistant" or entry.get("isSidechain"):
                    continue
                message = entry.get("message")
                if not isinstance(message, dict):
                    continue
                if message.get("model") == "<synthetic>":
                    continue
                usage = message.get("usage")
                if isinstance(usage, dict):
                    latest = usage
    except OSError:
        return None

    if latest is None:
        return None

    def count(key):
        value = latest.get(key)
        return value if isinstance(value, int) else 0

    return (
        count("input_tokens")
        + count("cache_read_input_tokens")
        + count("cache_creation_input_tokens")
        + count("output_tokens")
    )


def state_path(session_id):
    """Where the last-warned size is remembered.

    Temp storage on purpose: this state is only meaningful for one session in
    one container, and both are gone together. Keeping it out of the repo also
    keeps the hook from ever dirtying the working tree, which /checkpoint and
    the Stop hook both check.
    """
    safe = "".join(c for c in str(session_id) if c.isalnum() or c in "-_") or "unknown"
    return os.path.join(tempfile.gettempdir(), f"darkmyst-ctx-warn-{safe}.json")


def last_warned_at(path):
    try:
        with open(path, encoding="utf-8") as handle:
            value = json.load(handle).get("warned_at")
            return value if isinstance(value, int) else 0
    except (OSError, ValueError, AttributeError):
        return 0


def remember(path, tokens):
    try:
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"warned_at": tokens}, handle)
    except OSError:
        pass  # A forgotten warning repeats; a crashed hook blocks the turn.


def message(tokens, limit, repeat):
    size = f"~{tokens // 1000}k tokens"
    if repeat:
        return (
            f"[context budget] This session is now at {size}, well past the "
            f"{limit // 1000}k checkpoint budget and still growing. Finish or park "
            "the step in hand, then run /checkpoint and continue in a new session. "
            "Do not start anything new here — from this size on, what gets carried "
            "forward is a summary, and this project's record is specific measured "
            "numbers that a summary rounds off."
        )
    return (
        f"[context budget] This session has passed {limit // 1000}k tokens of "
        f"context ({size}). Tell the user, and once the step in hand is at a clean "
        "stopping point, run /checkpoint and start a new session from the handoff "
        "it writes. Handing over deliberately keeps the exact state — branch, "
        "measured numbers, in-flight agents, open decisions — which an "
        "auto-summary at an arbitrary moment does not."
    )


def main():
    raw = sys.stdin.read()
    try:
        payload = json.loads(raw) if raw.strip() else {}
    except json.JSONDecodeError:
        return

    transcript_path = payload.get("transcript_path") or ""
    if not transcript_path:
        return

    tokens = context_tokens(transcript_path)
    if tokens is None:
        return

    limit = budget()
    if tokens < limit:
        return

    path = state_path(payload.get("session_id"))
    warned_at = last_warned_at(path)
    if warned_at and tokens < warned_at + RE_WARN_STEP:
        return

    remember(path, tokens)
    json.dump(
        {
            "hookSpecificOutput": {
                "hookEventName": "UserPromptSubmit",
                "additionalContext": message(tokens, limit, repeat=bool(warned_at)),
            }
        },
        sys.stdout,
    )


if __name__ == "__main__":
    try:
        main()
    except Exception:
        pass
    sys.exit(0)
