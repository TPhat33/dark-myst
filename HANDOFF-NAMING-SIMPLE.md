# Naming Proposal, pass 3: easy to say

> **Status: a proposal for the project owner. Nothing in the game has changed.**
> No `content/*.json` or `docs/*.md` file was touched. All names are player-facing display text, and code ids
> (`mat_myst_core`, `EchoShard`, `/summon/attune`, `Ember`, …) stay as they are.
>
> This revises [HANDOFF-NAMING-EN.md](HANDOFF-NAMING-EN.md) (pass 2, English-first). The earlier Thai round is
> [HANDOFF-NAMING.md](HANDOFF-NAMING.md). **★ = lead pick.** The last section is the complete final list on its own.

## What changed and why

The owner's feedback was *"use Gold directly; 'The Toll' is hard to say,"* followed by *"I want the whole set to be
easy to say, but the character names are fine."*

The test for this pass: **could a player in any country say the word out loud to a friend on first sight, the way they say "Gold"?**

| | Before (pass 2) | Now | Why |
| --- | --- | --- | --- |
| Gold | Grave Silver | **Gold** | Owner's decision. |
| Gems | Black Pearls | **Pearls** | One word. Dropping "Black" also removes the Disney flag. |
| Energy | Lanternlight | **Oil** | One syllable. The lantern still runs on it. |
| Currency umbrella | The Toll | **(none)** | Dropped. Players only ever name the single currencies. |
| Evolve materials | Ash Sigil / Mist Core | **Seal / Core** | One short word each. |
| Summon cluster | Call / Unanswered / True Name / Memento / Recall / Effigy | **Call / Pity / Pick / Shards / Recall / Doll** | Six things to learn became **two flavor words that share a root (Call → Recall)** plus four plain words. |
| Affinities | Pyre / Shade / Thorn / Deep / Dawn / Dust | **Fire** / Shade / Thorn / Deep / Dawn / Dust | Only Pyre changed. It was the one uncommon word with unclear spelling-to-sound, and it had a trademark flag. |
| Reforge item | Crossroads Stone | **Reset Stone** | Says what it does. |
| Kept | Ashmist, Legacy, all 14 characters, enemies, the Ashfields | same | Already short and plain. |

**Net result:** a new player now meets only **five invented or flavored words** across the whole economy and summon
system: Pearls, Oil, Seal, Call and Recall. Every other word is either a plain word or a standard gacha term.

---

## Per-category changes

### Gems (premium): Black Pearls → Pearls

| | Name | Easy to say because… |
| --- | --- | --- |
| ★ | **Pearls** | It is one common word and needs no explanation, because a pearl already means "precious." The link to the drowned sea and the Deep affinity is kept. |
| | Gems | This is the fully plain word. It carries no world at all. |
| | Shards | Short, but it would collide with duplicate **Shards** (see Summon below). Do not use it for both. |

Flags: dropping "Black" clears the Disney *Black Pearl* note from pass 2. No major gacha uses "Pearls" as its
premium currency, as far as I know. Draw the icon as a dark, glossy pearl so it never looks like the cloudy **Core**.

### Expedition energy: Lanternlight → Oil

| | Name | Easy to say because… |
| --- | --- | --- |
| ★ | **Oil** | It is one syllable that everyone knows. "Out of Oil" is instantly clear. Oil is what the lantern burns to enter the mist, so the pass-2 image stays and the word gets shorter. Icon: a small oil flask or lantern with a fill gauge. |
| | Light | Just as short and more atmospheric. But it collides in meaning with the Dawn affinity, and "light" is also an everyday UI word (light mode, light attack). |
| | Stamina | The plain genre term. It is safe, but it has no flavor. |

Flag: *Darkest Dungeon*'s "Torch" is a different word and a different mechanic, so there is no conflict.

### Currency umbrella: The Toll → dropped

| | Option | Why |
| --- | --- | --- |
| ★ | **No umbrella name** | Players say "gold," "pearls" and "oil," and no one ever says the name of the system. An umbrella is one more word to teach and it does no work. The owner also flagged "Toll" as hard to say. |
| | Purse | This is the only candidate if a UI header is ever needed (for example, a wallet screen title). It is one plain word. |

Side effect: pass 2 listed **Knell** as an R5 name because it tied into the Toll. That tie is gone. **Wake** remains the R5 pick.

### Evolve materials: Ash Sigil / Mist Core → Seal / Core

These appear on the materials screen, so a little flavor is fine. One short word still reads better.

| Item | | Name | Easy to say because… |
| --- | --- | --- | --- |
| `mat_ashen_sigil` | ★ | **Seal** | One syllable and a common word. "Needs 20 Seals" is easy to read and to say. The tooltip can carry the ash: "A seal pressed from Ashfields ash." |
| | | Sigil | The pass-2 head noun. Gamers know it, but many non-native readers stumble on the soft *g* (SIJ-il). |
| | | Cinder | Two easy syllables, and "ash" is built in. It is a good backup if Seal feels too plain. |
| `mat_myst_core` | ★ | **Core** | One syllable, and already familiar from other games as "a rare crafting core." The tooltip carries the mist. |
| | | Mist Core | Fine as-is on a materials screen. Use it if the team wants the title word to show up in the inventory. |

Flag (low): *Fire Emblem Heroes* has "Sacred Seals" as items. "Seal" alone is a generic word.

### Summon system: six words → one root plus plain terms

In pass 2 the player had to learn six new words at once, and that is the part the owner felt most. The fix has two parts:
1. **Keep one flavored root, "Call,"** because it does real work. It is the button, the tab and the tagline
   ("Call What Remains"). Its partner **Recall** is the only other flavored word, and a player hears the link just by saying the two.
2. **Use the plain gacha term** everywhere a standard term already exists. A fantasy synonym for *pity* only means
   a new player has to translate it back.

| Mechanic | Code id | Before | ★ Now | Other candidates | Easy to say because… |
| --- | --- | --- | --- | --- | --- |
| Pull (tab + button) | summon | The Calling / Call | ★ **Call** ("Call ×1", "Call ×10"); the tab is also just "Call" | Summon (plain) | One syllable. No separate system name is needed: the tab is simply "Call." |
| Pity counter | pity | Unanswered | ★ **Pity** ("Pity 43 / 60") | Unanswered | **Plain term, recommended.** Gacha players worldwide already say "pity," and a new player learns it once here and understands it in every other game. The regulator-facing disclosure also no longer needs a gloss. "Unanswered" was four syllables for a number. |
| Spark | spark | True Name | ★ **Pick** ("Pick 132 / 150", then "Pick one") | Spark · Name | It explains itself: at 150 you pick. "Spark" is Granblue and FEH jargon, and many players don't know it. Of the plain terms, it is the one that needed replacing. |
| Duplicate shard | `EchoShard` | Memento | ★ **Shards** ("+12 Shards") | Memento · Tokens | This is the standard word for duplicate currency, so it needs no teaching. It stays clear of *Echo* (the Ash Echoes / Wuthering Waves / Bloodborne trademark concern) and of Persona 5's *Mementos*. |
| Attune | `/summon/attune` | Recall | ★ **Recall** (button "Recall", then "Legacy +3") | Level Up · Bind | **Kept on purpose.** Call → Recall is the root the owner's earlier feedback liked. Spending Shards makes the unit recall who it was, which raises its **Legacy**. |
| Myst Effigy | effigy | Effigy | ★ **Doll** (shown as "Fetter Doll") | Effigy · Idol | One syllable, where *Effigy* (EF-ih-jee) is a mid-frequency word with a soft *g*. A carved stand-in doll is a familiar dark-fantasy image. |

**The loop, which a player can now say in one breath:** *Call to get units. Pity guarantees a strong one by 60.
At 150 you Pick one. Duplicates give Shards, and Shards let a unit Recall its past, which raises its Legacy. A Doll
stands in for a copy at evolve.*

Flags:
- *Recall* (low): in MOBAs such as League of Legends, "recall" means teleporting home. The meaning here is different, and the word is common English.
- *Doll*: it may read as cute to some players. If art or the team finds it too light, fall back to **Effigy**, which is safe from a trademark point of view and only a little harder to say. Avoid **Idol**: ⚠ CULT (religious images), and it collides with the K-pop "idol" game genre.
- Rate disclosure: because Pity and Shards are now the plain terms, only **Call (summon)** and **Pick (spark, 150 calls)** need a parenthetical on the odds screen.

### Affinities: only Pyre changes

| id | Before | ★ Now | Note |
| --- | --- | --- | --- |
| Ember | Pyre | ★ **Fire** (alternatives: Pyre, Cinder) | *Pyre* is the one word in the set that many ESL players don't know, and its spelling doesn't tell you how to say it (PIE-er). It also carried the Supergiant *Pyre* trademark note and the Hindu-rites note. **Fire** is the most-known word there is, and it still pairs against **Deep**. If the team would rather keep the "six rites" rule strictly, keeping Pyre is a defensible choice. |
| Umbral / Verdant / Tide / Radiant / Neutral | Shade / Thorn / Deep / Dawn / Dust | same | Already one syllable and common. |

Fire is generic and no one owns it as a trademark. Genshin uses *Pyro*, not "Fire".

### Reforge Stone: Crossroads Stone → Reset Stone

| | Name | Easy to say because… |
| --- | --- | --- |
| ★ | **Reset Stone** | Everyone knows the word "reset," and it says exactly what the item does. Tooltip: "Reset a focus choice." |
| | Reforge Stone (current) | It is already easy to say and needs no change in content. But it implies smithing, and nothing here is forged. |
| | Crossroads Stone | It is two words and abstract, so it is dropped. |

### Unchanged (checked, no change needed)

- **Game title: Ashmist.** Two easy syllables, and anyone can spell it after hearing it. A simpler title would need a
  common word that is hard to own in search. Keep it. Tagline: *"Call What Remains"*, which now also matches the summon button word.
- **Inherited bonus: Legacy.** One common word, and it pairs with Recall.
- **Stage and enemies:** The Ashfields ("Into the Ashfields"), Gloom Hound, Bone Archer, Rot Swarm, Crypt Acolyte,
  Crypt Warlock, Wailing Wisp, Drowned Warden, and the boss The Ashen Revenant. All are plain and readable, and none needed changing.
- **Characters:** all 14 call-names and epithets are carried over exactly as they were.

**Still rejected. Do not reintroduce:** Myst / Dark Myst, Ashveil, Mourncall, Echo (Ash Echoes / WuWa / Bloodborne),
Attune (Dark Souls), Shackleborn (Pathfinder), Mementos (Persona 5), Black Pearl (Disney), Rahu, Agni and other deity names,
and "___ Tide" currencies (WuWa).

---

## The whole set, all in one place

| Category | Code id / current | ★ Final display name |
| --- | --- | --- |
| Game title | Dark Myst | **Ashmist**. Tagline: "Call What Remains." |
| Affinities | Ember / Umbral / Verdant / Tide / Radiant / Neutral | **Fire / Shade / Thorn / Deep / Dawn / Dust** |
| Character 1 | Ashen Knight | **Vow, the Ashen Knight** (stage III: "Vow, Revenant Lord") |
| Character 2 | Grave Warden | **Tally, the Gravekeeper** |
| Character 3 | Thorn Maiden | **Rue, the Thorn Maiden** |
| Character 4 (R4) | Tide Oracle | **Ebb, the Stillwater Oracle** |
| Character 5 | Dawn Cantor | **Lark, the Dawnsinger** |
| Character 6 | Ember Adept | **Flint, the Ember Adept** |
| Character 7 | Pale Stalker | **Hush, the Pale Stalker** |
| Character 8 | Mire Hexer | **Leech, the Mire Witch** |
| Character 9 | Bramble Warden | **Barrow, the Bramble Sentinel** |
| Character 10 | Riptide Stormcaller | **Grudge, the Grudgetide** |
| Character 11 | Dawnrider | **Spur, the Dawnrider** |
| Character 12 | Ashfield Reaver | **Tatters, the Ashfield Reaver** |
| Character 13 | Hollow Warder | **Nil, the Empty Helm** |
| Character 14 (R4) | Shackleborn | **Fetter, the Breathbinder** |
| Featured banners | | Fetter first, then Ebb. Vow is the story lead and is not sold. First R5: **Wake** |
| Stage 1 | `stg_ashfields` | **The Ashfields** ("Into the Ashfields") |
| Enemies | | Gloom Hound · Bone Archer · Rot Swarm · Crypt Acolyte · Crypt Warlock · Wailing Wisp · Drowned Warden |
| Stage-1 boss | | **The Ashen Revenant** |
| Soft currency | gold | **Gold** |
| Premium currency | gems | **Pearls** |
| Expedition energy | energy | **Oil** |
| Currency umbrella | | *(none)* |
| Evolve material | `mat_ashen_sigil` | **Seal** |
| Evolve material (rare) | `mat_myst_core` | **Core** |
| Focus undo item | Reforge Stone | **Reset Stone** |
| Inherited bonus | `inheritedBonusPerMille` | **Legacy** ("Legacy 214 / 300") |
| Summon (tab + button) | summon | **Call** ("Call ×1", "Call ×10") |
| Pity counter | pity | **Pity** ("Pity 43 / 60") |
| Spark | spark | **Pick** ("Pick 132 / 150", then "Pick one") |
| Duplicate shard | `EchoShard` | **Shards** |
| Attune | `/summon/attune` | **Recall** |
| Effigy | Myst Effigy | **Doll** ("Fetter Doll"). Fallback: Effigy |

### Before locking

1. Run a formal trademark search on **Ashmist**. This is unchanged from pass 2.
2. The pass-2 flags on *Pyre*, *Black Pearls* and *Mementos* are **resolved**, because none of those words is in the final set any more.
3. Get a quick art and tone check on **Doll**. If it reads too cute, use Effigy.
