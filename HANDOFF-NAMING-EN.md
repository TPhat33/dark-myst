# Naming Proposal (English-first): title, affinities, characters, enemies, stage, currencies, summon system

> **Status: a proposal for the project owner to choose from. Nothing in the game has changed.**
> No `content/*.json` or `docs/*.md` file was touched. Every name here is **player-facing display text**;
> code ids (`line_ashen_knight`, `Ember`, `mat_myst_core`, `EchoShard`, `/summon/attune`, …) stay as they are.
>
> This round **English is the primary, final in-game language**. The names were written in English
> for a global audience. They are not translations of the Thai picks in
> [HANDOFF-NAMING.md](HANDOFF-NAMING.md). Where a Thai pick's *idea* still worked, I kept the idea and rewrote
> it in English. Thai glosses appear in parentheses only as internal reference for the dev team.
>
> **★ = my lead pick** in every category. The last section, "If you want just one column," gives a single answer for each.

## Principles used across every category

- **Tone.** Heavy, mysterious, a little mournful. The words are concrete and physical (ash, pyre, thorn,
  lantern, silver) rather than big empty ones (legend, eternal, supreme, divine). The names are not cute and
  not "EPIC."
- **One world.** The three substances already in the content are the backbone: **ash** (stage 1, the
  Ashen Knight line, the boss), **mist** (Myst Core, Myst Effigy, the working title), and **deep/still water**
  (Tide Oracle, Drowned Warden). This round adds one theme that English carries well: **remembering the dead**.
  Characters are *called back*, duplicates help them *recall* who they were, and the evolve bonus is their
  *legacy*. That theme fits the game's first selling point: characters with an identity and a recorded history.
- **Flavor text is the source.** Each character's call-name comes from its own line in `characters.json`.
- **Global readability.** I prefer one- or two-syllable words that a non-native reader can say on first
  sight. I avoided `th`/`r` clusters, silent letters, and words that sound like common English words
  ("Yew" reads as "you," and "Morning" sounds like "Mourning").
- **Flags.**
  - ⚠ **TM**: close to an existing game, franchise, or trademarked in-game term.
  - ⚠ **CULT**: a real religious or cultural term.
  - ⚠ **SAY**: hard to pronounce or remember for non-native readers.

  I spot-checked the most important candidates with web searches (results are cited where relevant). **That is
  not legal clearance.** Before launch, the final title and any store-facing term need a proper
  USPTO/EUIPO/app-store trademark search.

### What changed since the Thai round, and why

| Last round | Problem once the audience is global | This round |
| --- | --- | --- |
| Affinity **Rahu** (and the Pali/Sanskrit set: *Akkhi* ≈ Agni) | Rahu and Agni are deities in living Hindu/Buddhist practice. In a Thai context they read as folk culture. Worldwide, a deity used as a damage type reads as appropriation. ⚠ CULT | Dropped. The whole set is now plain English. |
| Title **Ashmyst** | *Myst* is Cyan's active trademark. The "-myst" spelling also sits near Epic Seven's "Mystic" summons and Raid's "Mystery Shards". ⚠ TM | **Ashmist**, spelled with an *i* |
| **Echo** (duplicate shard) | *Ash Echoes* is a live global gacha whose units are "Echomancers" and whose pull currency is "Resonance Clue" ([source](https://gachagames.miraheze.org/wiki/Ash_Echoes), [source](https://www.ldplayer.net/blog/ash-echoes-gacha-system.html)). *Wuthering Waves* has "Echoes" plus "Resonance Chains" for duplicates. Bloodborne's currency is "Blood Echoes". An **ash**-themed gacha with **Echoes** that you **attune** lands right between them. ⚠ TM | **Memento**, spent to **Recall** (section 7) |
| **Attune** | *Dark Souls* uses "Attunement" for spell slots. *Wuthering Waves* uses "Tune" and "Resonance". ⚠ TM (mild) | **Recall** |
| Line name **Shackleborn** | "Shackleborn" is an existing Paizo *Pathfinder* heritage, also used in *Pathfinder: Wrath of the Righteous* ([source](https://pathfinderwiki.com/wiki/Shackleborn), [source](https://2e.aonprd.com/Feats.aspx?ID=2451)). ⚠ TM | Epithet **the Breathbinder** |
| **Dawn Cantor** | A *cantor* is a clergy role in Jewish worship. That is fine in-world, but it is a real religious office. ⚠ CULT (mild) | Epithet **the Dawnsinger** |
| **Black Pearls** (premium currency) | Only a mild echo of Disney's *Black Pearl* ship | Kept, with a note |
| Four "Warden/Warder" names (Grave, Bramble, Hollow, Drowned) | English has the same collision the Thai version had | Three player-side ones renamed. Only the enemy keeps "Warden". |

---

## 1. Game title

| | Title | One-line pitch | Checks |
| --- | --- | --- | --- |
| ★ | **Ashmist** | Two syllables made of two words every English learner knows. They are the world's two substances: ash is what the fighting leaves, and mist is where the characters come from. It is a coined compound, so it can own its search results. | A web search found only a Scratch username and a YouTube handle, and no released game ([search](https://scratch.mit.edu/users/Ashmist/)). Nearby titles to be aware of: *Ash Echoes* (gacha), *Ashwalkers*, *Moonmist*. None of them is identical. Needs a formal TM search. |
| | **Mourncall** | This title names the core loop: you call the lost back out of the mist. It is the most distinctive of the options. | ⚠ TM: *Court of the Dead: Mourners Call* is a Sideshow board game ([source](https://boardgamegeek.com/boardgame/248918/court-of-the-dead-mourners-call)), and an Unreal Marketplace asset is also named "Mourncall" ([source](https://forums.unrealengine.com/t/mastertob-mourncall/2704395)). ⚠ SAY: non-native readers may confuse "mourn" with "morning". |
| | **Heirs of Ash** | The game *is* inheritance: evolve passes power forward, duplicates are never wasted, and history is recorded. | The words are generic, so it will be hard to own in search. It is close to *Ash of Gods* and *Ashes of Creation*. |
| | **What the Ash Remembers** | This title is the most literary, and it is lifted from the stage-III flavor line. | Too long for an icon or store header. Better as a **tagline**. |
| | ~~Ashveil~~ | Rejected. *Ashveil* is a 5★ character in *Honkai: Star Rail* and also an itch.io action RPG ([source](https://honkai-star-rail.fandom.com/wiki/Ashveil), [source](https://jayeci.itch.io/ashveil)). ⚠ TM | |
| | ~~Dark Myst~~ (current) | Rejected for launch. It uses *Myst* directly (⚠ TM, Cyan), and "Dark ___" is the most crowded title pattern in the genre. | |

**Why Ashmist leads:** it is short enough for an icon and a store header, anyone can spell it after hearing it once,
it keeps "mist" so `mat_myst_core` and the Effigy still fit the title, and it avoids the Cyan trademark.
**Tagline:** *"Ashmist — Call What Remains."*
(Thai: หมอกเถ้า — ขานเรียกสิ่งที่ยังเหลือ)

---

## 2. Affinities: six words that work as one system

Each set below fixes a **single rule** that all six words obey, so the words read as a cosmology rather than as color labels.
A code-side note: the ids `Ember/Umbral/Verdant/Tide/Radiant/Neutral` do not need to change.

### Set A ★ "The six rites": how the world keeps its dead

Every affinity is a single one-syllable English noun, and each is a way the dead are kept.

| id | ★ Name | Meaning in-world | Fits existing content |
| --- | --- | --- | --- |
| Ember | **Pyre** | Fire built for the dead, not for warmth | Ashen Knight's burnt chapel, the Ashen Revenant |
| Umbral | **Shade** | Both a shadow and a ghost | Grave Warden, Pale Stalker, Gloom Hound |
| Verdant | **Thorn** | What grows over the graves | Thorn Maiden, Bramble Warden ("something kept feeding the roots") |
| Tide | **Deep** | Where the drowned lie | Tide Oracle, Drowned Warden |
| Radiant | **Dawn** | The light that comes back whether or not anyone sings for it | Dawn Cantor ("it would rise anyway"), Dawnrider |
| Neutral | **Dust** | What everything becomes in the end, which makes it neutral in a mournful way rather than an empty slot | Reaver, Warder, Shackleborn |

**Why this set leads:**
1. All six are one syllable and 4–5 letters. They fit tiny UI chips and they are among the first thousand words
   most English learners meet.
2. There is one rule behind all six, so new content can extend it ("is this a Pyre unit or a Dust unit?").
3. Shade and Dawn, and Pyre and Deep, pair as opposites without a lecture.
4. No trademark collision among *affinity systems*. Genshin uses Pyro/Hydro/Anemo…, Wuthering Waves uses Fusion/Spectro/Havoc…,
   Destiny uses Solar/Arc/Void/Stasis/Strand, AFK Arena/Journey uses Lightbearer/Wilder/Graveborn/Celestial/Hypogean,
   and HSR paths include Nihility and Remembrance.

⚠ TM (low): *Pyre* is also a 2017 Supergiant game title. As an affinity label this is very low risk. If legal
dislikes it, **Cinder** drops in without breaking the rule (it becomes two syllables, but still "what fire leaves").
⚠ CULT (low): funeral pyres are part of Hindu rites. Here the word is plain English and generic, so no
flag beyond noting it.
(Thai: เชิงตะกอน / เงา / หนาม / ห้วงลึก / รุ่งสาง / ธุลี)

### Set B "What the old world left": remnants, same length

| Ember | Umbral | Verdant | Tide | Radiant | Neutral |
| --- | --- | --- | --- | --- | --- |
| Cinder | Dusk | Root | Brine | Dawn | Iron |

This set has the clearest lore logic for **Neutral = Iron**. It explains why the Neutral lines are a Reaver, a Warder and a
chained Controller: blade, armor, shackle, no magic, only metal. **Dusk/Dawn** is a neat pair.
Weaknesses: *Brine* is a less-known word (⚠ SAY, mild), and Dusk is softer than the others.

### Set C: keep the current ids as display text

Ember · Umbral · Verdant · Tide · Radiant · Neutral. This costs nothing, but it mixes nouns (Ember, Tide) with
adjectives (Umbral, Verdant, Radiant). *Umbral* and *Verdant* are ⚠ SAY for non-native readers.
*Radiant* is heavily used by *Valorant* (the top rank and its lore term) and by Sanderson's *Stormlight*. "Neutral" reads as "no element".

### Set D (considered, not recommended): Latinate, Genshin-style

Pyra · Umbra · Verda · Mare · Lux · Nihil. It sounds "gacha-standard" precisely because it copies the Genshin/WuWa
convention. It is also ⚠ TM-adjacent (*Nihil* is close to HSR's *Nihility*) and less readable than plain English.
Listed only to show it was weighed.

---

## 3. Characters: 14 lines

Each line gets two parts:
- **Call-name**: short. It is what players say ("I finally got Fetter!").
- **Epithet**: an archetype title shown in lists and on cards, formatted as *Fetter, the Breathbinder*.

The epithets work with every call-name set. Three call-name sets follow.

### 3.1 Epithets (shared by all call-name sets)

| # | Current line | Affinity / role / rarity | ★ Epithet | Alternative | Notes |
| --- | --- | --- | --- | --- | --- |
| 1 | Ashen Knight | Ember / Vanguard / R3 | **the Ashen Knight** | the Oathsworn | Kept. It is the stage-I name of a line whose stage III is *Ashen Revenant Lord*, and that link is story. ⚠ TM (low): Dark Souls III's "Ashen One". The phrase is different, but do not drift closer. |
| 2 | Grave Warden | Umbral / Guard / R3 | **the Gravekeeper** | the Counter of the Dead | Renamed so it no longer collides with "Warden" ×3. It also avoids *Elden Ring*'s "Grave Warden Duelist". |
| 3 | Thorn Maiden | Verdant / Bruiser / R2 | **the Thorn Maiden** | the Last Gardener | Kept. It is a clear archetype. The alternative comes from her flavor text ("outlives its gardener"). |
| 4 | Tide Oracle | Tide / Mender / **R4** | **the Stillwater Oracle** | the Tide Oracle | "Stillwater" makes her image concrete (she reads futures in *standing* water). |
| 5 | Dawn Cantor | Radiant / Mender / R3 | **the Dawnsinger** | the Sun-Caller | Replaces *Cantor* (⚠ CULT, a Jewish clergy role). "Dawnsinger" is plain and self-explanatory. |
| 6 | Ember Adept | Ember / Breaker / R3 | **the Ember Adept** | the Firestarter | Kept. "Firestarter" is ⚠ TM-adjacent (Stephen King's novel, a Prodigy song), so it is only an alternative. |
| 7 | Pale Stalker | Umbral / Assassin / R3 | **the Pale Stalker** | the Soundless | Kept. It is clean and readable. |
| 8 | Mire Hexer | Verdant / Hexer / R2 | **the Mire Witch** | the Mire Hexer | "Witch" is universally understood. "Hexer" is ⚠ TM (low), because *The Witcher* was released in German and in its 2001 film as *Der Hexer / The Hexer*. |
| 9 | Bramble Warden | Verdant / Sentinel / R3 | **the Bramble Sentinel** | the Rootwarden | "Warden" is kept only for the enemy. The epithet rolls naturally into stage II, *Heartwood Bastion*. |
| 10 | Riptide Stormcaller | Tide / Stormcaller / R3 | **the Grudgetide** | the Tide-Reader | This is a coined word from his flavor text ("reads the tide's grudges"). ⚠ TM: "Stormcaller" is a *Destiny 2* Warlock subclass, so do not use it as the displayed epithet. |
| 11 | Dawnrider | Radiant / Herald / R3 | **the Dawnrider** | the Outrider | Kept. It is a coined compound, and a web search found only a minor itch.io character name. Stage II, *Dawnrider Exalted*, stays consistent. |
| 12 | Ashfield Reaver | Neutral / Reaver / R3 | **the Ashfield Reaver** | the Turncloak | Kept, because it names stage 1 as his home. Avoid "the Bannerless": *banner* is also the gacha UI word, and the joke would confuse. |
| 13 | Hollow Warder | Neutral / Warder / R3 | **the Empty Helm** | the Hollow Guard | Armor with no one inside. This also moves away from "Hollow" (the *Hollow Knight* association) and from "Warder". |
| 14 | Shackleborn | Neutral / Controller / **R4** | **the Breathbinder** | the Chainbound | ⚠ TM: "Shackleborn" is a *Pathfinder* term (Paizo), so replace it for global launch. The new epithet comes straight from his flavor text ("chains an enemy's next breath"). |

After these changes, "Warden" appears **once** (the enemy Drowned Warden). No two player-side epithets share a
head noun except Knight/Oracle-style archetypes that are clearly different.

### 3.2 Call-names, Set A ★ "Called by what they became"

**World rule.** Real names were lost with the old world. People are called by **one English word**: the
thing they turned into. The rule scales: for every new line, the writers only ask *"what did this one become?"*

| # | ★ Call-name | Card reads | From the flavor text | Global notes |
| --- | --- | --- | --- | --- |
| 1 | **Vow** | Vow, the Ashen Knight | "The chapel is ash; the oath is not." His name is the one thing that did not burn. | Known word, 1 syllable. ⚠ TM (very low): Obsidian's *Avowed* is a different word. |
| 2 | **Tally** | Tally, the Gravekeeper | "Counts the dead every night. The number keeps changing." At stage II he "stopped counting." | Easy to say. The name is quietly sad. |
| 3 | **Rue** | Rue, the Thorn Maiden | Rue is a garden herb and also a word for regret. Her gardens outlive their gardeners. | ⚠ SAY (low): say "roo". It is the most melancholy name on the list. |
| 4 | **Ebb** | Ebb, the Stillwater Oracle | The tide going out. She sees futures and "rarely likes them". | Three letters and visually unique. |
| 5 | **Lark** | Lark, the Dawnsinger | Larks sing at dawn. "Nobody has told her it would rise anyway." | ⚠ TM (very low): ByteDance's *Lark* office app is a different category. |
| 6 | **Flint** | Flint, the Ember Adept | "The second-easiest way to set something alight." Flint is the igniter, not the fire. | Last round's Thai idea (ชนวน, fuse) was kept as an idea. The obvious English words were rejected: *Tinder* (⚠ TM, the dating app), *Kindle* (⚠ TM, Amazon), *Spark* (collides with the summon term), *Wick* (John Wick). |
| 7 | **Hush** | Hush, the Pale Stalker | "Arrives before the sound of arriving." | ⚠ TM (low): DC has a Batman villain named Hush, but it is a single common word. |
| 8 | **Leech** | Leech, the Mire Witch | "Trades in small curses. Volume is where the money is." She drains a little at a time from everyone. | Gamers know the word from "life leech". Alternative: **Midge** (tiny swarming marsh bites), but it is ⚠ SAY (a less-known word). |
| 9 | **Barrow** | Barrow, the Bramble Sentinel | "Rooted itself over the old battlefield." A barrow is a burial mound. | ⚠ Minor: RuneScape's "Barrows" and the word "wheelbarrow". The rejected *Yew* (the graveyard tree) is ⚠ SAY, because it sounds exactly like "you" in UI text. |
| 10 | **Grudge** | Grudge, the Grudgetide | "The grudges are more accurate." | Memorable and slightly dark-comic. ⚠ TM (low): *The Grudge* films, but it is a single common word. Rejected: *Squall* (Final Fantasy VIII), *Undertow* (already the Drowned Warden's skill). |
| 11 | **Spur** | Spur, the Dawnrider | "Rides ahead of the army so the army arrives already winning." | Rejected: *Herald* (a role name), *Harbinger* (⚠ TM, Genshin's Fatui Harbingers), *Banner* (the gacha UI word). |
| 12 | **Tatters** | Tatters, the Ashfield Reaver | "Fought for every banner going." What he has left is torn flags and a torn coat. | The plural nickname reads naturally in English ("Old Tatters"). |
| 13 | **Nil** | Nil, the Empty Helm | "Doesn't flatter anyone's lucky streak." Nothing inside, and nothing given. | Known worldwide (a sports score, "nil"). *Husk* was dropped because *Hush*/*Husk* are too close. |
| 14 | **Fetter** | Fetter, the Breathbinder | "Chains an enemy's next breath before it can spend it." | ⚠ SAY (low): a less common word, but spelled as it sounds (FET-er). *Tether* was rejected (⚠ TM, the Tether/USDT cryptocurrency). |

**Why Set A leads:**
1. Every name is an image, so players remember it without learning lore.
2. Lengths vary from 3 to 7 letters, so names don't blur in a list.
3. Initial letters are spread out. Only T (Tally/Tatters), L (Lark/Leech) and F (Flint/Fetter) repeat, and
   each pair differs in length and sound.
4. English common nouns have no cross-language pronunciation traps. Invented fantasy names do: *Aeryth*,
   *Xyrael*, and the like.

### 3.3 Call-names, Set B: invented given names (old-European sound, global-safe)

These feel more like people and less like concepts. They sit closer to genre convention, but they are harder to tell apart.
All are two syllables or fewer, use no `th`, and avoid famous characters I know of.

| 1 Osric | 2 Mordan | 3 Briony | 4 Nerys | 5 Oriel | 6 Cade | 7 Silas |
| --- | --- | --- | --- | --- | --- | --- |
| **8 Tamsin** | **9 Garrow** | **10 Maren** | **11 Lucan** | **12 Hale** | **13 Noll** | **14 Harrow** |

Removed from last round's Western set: *Brand* (⚠ TM: **League of Legends' fire mage "Brand"**, an exact
role match for the Ember Adept), *Mora* (⚠ TM: Genshin's gold currency), *Ogham* (⚠ CULT: a real historical Irish
script), *Vesper* (a crowded title), and *Auren* (it sounds like *Oriel*). ⚠ Watch *Garrow*/*Harrow*, which rhyme. Swap one if Set B is chosen.

### 3.4 Call-names, Set C: no call-names, epithet only

In the style of *Darkest Dungeon*, the unit simply *is* "the Gravekeeper". Players learn nothing new, and localization
is cheapest. But it gives up selling point #1 (memorable individuals) and makes the "remembering" theme of
the summon system (section 7) weaker. Recommended only if playtests show that call-names confuse players.

### 3.5 Featured-banner and legendary candidates

Per [12-summon-spec.md](docs/12-summon-spec.md), the R4 lines are the only ones that can front a rotating featured banner
until the first R5 ships.

| Name | Suitability | Why |
| --- | --- | --- |
| **Fetter, the Breathbinder** (R4) ★ | First featured banner | A hard, clipped sound, a clear image (chains), and a controller-fantasy card. He already rolls as R4. |
| **Ebb, the Stillwater Oracle** (R4) | Second banner, or paired with Fetter | A strong character hook ("sees the future, doesn't like it"). She and Fetter give a visual and tonal pair. |
| **Vow, the Ashen Knight** (R3) | **Story lead, not a banner** | His stage III is *Ashen Revenant Lord* and the stage-1 boss is *The Ashen Revenant*. The player is raising a knight into the thing they just killed. Give him in the tutorial and don't sell him. |

**First R5, not yet in the game.** Keep the one-word rule, but make R5 words **larger than the others**. Suggestions:
- **Wake**: a triple meaning (a funeral wake, a ship's wake, waking the dead). My pick.
- **Eclipse**: the English, non-religious successor to last round's *Rahu*. It suits Shade.
- **Knell**: a funeral bell. It ties into the "Toll" currency system in section 6.
- *Requiem*: ⚠ CULT (low), because it is a Catholic mass.

---

## 4. Enemies (8, including the stage boss)

The existing English names are mostly good already. The job here is to keep what works, fix collisions, and flag risks.
Early enemies should stay **instantly legible**, because a player meeting "Bone Archer" needs no tooltip.

| id | ★ Lead | Alternatives | Reasoning / flags |
| --- | --- | --- | --- |
| Gloom Hound | **Gloom Hound** | Mist Hound · Grave Hound · Barghest | The first enemy players meet, and a clean read. *Barghest* (the English folklore black dog) has great mood but is ⚠ SAY. "Gloom" is a common word, and the *Gloomhaven* overlap is negligible. |
| Bone Archer | **Bone Archer** | Bonestring Archer | "Bonestring" (a bow strung with sinew) is nicer, but first-stage legibility wins. |
| Rot Swarm | **Rot Swarm** | Blowfly Host · Carrion Swarm | *Carrion Swarm* is ⚠ TM (low): a WoW spell and the 2020 game *Carrion*. |
| Crypt Acolyte (Radiant) | **Crypt Acolyte** | Crypt Chanter · Taperbearer | "Acolyte" is standard fantasy vocabulary and is OK globally (⚠ note: the 2024 *Star Wars: The Acolyte* is a title, not a creature). A light-affinity cultist in a crypt is a nice irony, so keep it. Last round's warning about Buddhist novice terms does not apply in English. |
| Crypt Warlock | **Crypt Warlock** | Tomb Warlock | Avoid "Crypt *Caller*": *Call* is the summon verb. Avoid "Hexer": it would collide with the Mire Witch. |
| Wailing Wisp | **Wailing Wisp** | Weeping Light · Keening Wisp | *Keening* is ⚠ CULT (low), since it comes from Irish mourning practice (*caoineadh*) and is also a less-known word. |
| The Ashen Revenant (**boss**) | **The Ashen Revenant** | The Unbroken Vow (only with call-name Set A) | Rejected: *The Unburnt* (⚠ TM, Game of Thrones) and *Lord of Cinder* (⚠ TM, Dark Souls' "Lords of Cinder"). The alternative makes the link to Vow explicit (see below). |
| Drowned Warden | **Drowned Warden** | Tidewarden · Drowned Jailer | Now the only "Warden" in the game, so it can keep the word. |

**The boss and the stage-III form.** The stage-1 boss is *The Ashen Revenant*. The playable Ashen Knight line
ends at stage III as *Ashen Revenant Lord*. Keep the English stems identical, because the echo is the story. Suggested card
text for stage III with Set A: **"Vow, Revenant Lord"**, dropping "Ashen" to keep the card short. The flavor line
"What walks out of the ash is not what walked in" then lands on a character the player has raised themselves.

---

## 5. Stage 1 location (`stg_ashfields`)

| | Name | Reasoning |
| --- | --- | --- |
| ★ | **The Ashfields** | Short and literal, and it anchors "Ashfield Reaver" and the Ash Sigil drop. Real towns named Ashfield (England, Australia) are harmless. |
| | The Oathfield | Ties stage 1 to Vow's oath. Good if the opening chapter is his story. |
| | The Burnt Chapel | The strongest image. ⚠ CULT (low): a burned church as a set piece is common in dark fantasy, but it is a Christian place of worship. OK as an **in-stage landmark** rather than the stage name. |
| | Cinderreach | Coined and ownable, but more "generic epic" than the others. |

The expedition label ("The Ashfields Expedition") becomes **"Into the Ashfields"**. It reads as an action on a button.
(Thai: ทุ่งเถ้า)

---

## 6. Currencies and resources: one in-fiction system

Collision checklist (these names are taken by major games and are avoided here):
- **Soft currency:** *Mora* (Genshin), *Shell Credit* (Wuthering Waves), *Geo* (Hollow Knight), *Runes* (Elden Ring), *Souls* (Dark Souls).
- **Premium currency:** *Primogem*, *Stellar Jade*, *Orundum*, *Pyroxene*, *Saint Quartz*, *Orbs* (FEH/PoE), *Crowns* (ESO), *Skystones* (Epic Seven), *X Particles* (Ash Echoes).
- **Energy:** *Resin*, *Trailblaze Power*, *Sanity*, *Waveplate*, *Enkephalin*.
- **Other:** *Obols* (Diablo IV, Hades), and *Radiant Tide* / *Lustrous Tide* (Wuthering Waves' pull currencies). **Never name a currency "___ Tide".**

### Set A ★ "The Toll": what the mist takes from those who walk in and come back

The system has one umbrella name, **the Toll**. It is both a price and the sound of a funeral bell.

| Resource | ★ Name | In-world | Icon reads as |
| --- | --- | --- | --- |
| Gold | **Grave Silver** | Old coin taken from the dead of the fallen world. Everyone uses it and no one mints it any more. | A tarnished coin, which says "money" instantly |
| Gems (premium) | **Black Pearls** | Dived from the drowned sea that swallowed the old world. Rare, and tied to the Deep affinity. | A dark pearl, which says "precious" instantly |
| Expedition energy | **Lanternlight** | You cannot walk into the mist without light, and the lantern slowly refills. Time-based regeneration explains itself. | A lantern with a fill gauge |
| `mat_ashen_sigil` | **Ash Sigil** | A seal pressed from Ashfields ash | A stamped seal |
| `mat_myst_core` | **Mist Core** | Mist condensed until it is solid. The *i* spelling matches the title (the code id stays `myst`). | A cloudy orb |

**Why this set leads:**
1. The three main names (Silver, Pearls, Lanternlight) differ in sound, image and first letter.
2. The premium currency is **deliberately not** "Mist Crystal", so players never confuse what they buy with the rare
   evolve material (Mist Core). This was carried over from last round's reasoning.
3. Two of the three contain the literal hint (silver, light), which helps non-native players.

Flags:
- ⚠ TM (low): *Black Pearl* is the ship in Disney's *Pirates of the Caribbean*. As a plural generic currency the risk is
  low. If legal objects, use **Deep Pearls**.
- ⚠ Genre note: *Darkest Dungeon*'s torchlight meter uses the same idea, light as the cost of the dark. Its term is
  "Torch" and it is an in-run meter, not energy, so there is no naming conflict.
- "Grave Silver": a web search found no game currency by this name.

(Thai: เงินป่าช้า / มุกดำ / แสงตะเกียง / ตราเถ้า / แก่นหมอก)

### Set B "Offerings" (the Thai round's idea, in English)

Ferry Coin · Tears · Candles · Ash Sigil / Mist Core. This set has the most atmosphere, but:
- ⚠ CULT: every term is funerary or ritual.
- ⚠ TM: *Darkest Dungeon II*'s "Candles of Hope".
- ⚠ TM (low): "Ferry coin" is Charon's *obol*, which is Hades/Diablo IV territory.
- Selling "Tears" or "Candles" for real money reads badly.

### Set C "Plain with one flavor word" (recommended fallback for global learnability)

**Gold · Black Pearls · Lanternlight · Ash Sigil · Mist Core**. Soft currency stays "Gold", which every player on
earth already understands. Only the premium currency and energy carry flavor. Choose this if playtests show new
players stumbling on "Grave Silver".

### Set D: fully literal

Gold · Gems · Energy · Sigils · Cores. No learning at all, and no world at all.

### Single items

**Reforge Stone** (undoes a wrong focus choice; see [03-evolve-spec.md](docs/03-evolve-spec.md))

| | Name | Reasoning |
| --- | --- | --- |
| ★ | **Crossroads Stone** | A focus choice is a fork in the road, and this stone takes the unit back to the crossroads. The word is known worldwide and the image is atmospheric, while the function is still guessable. |
| | Retrace Stone | The most self-explanatory ("retrace your steps"), but plainer, and "retrace" is a mid-frequency word. |
| | Reforge Stone (current) | Clear, but a well-worn term (WoW's reforging, Terraria's Reforge). It is also about smithing, and nothing here is forged. |
| | ~~Wayback Stone~~ | ⚠ TM: the Internet Archive's *Wayback Machine*. |

**Inherited bonus** (`inheritedBonusPerMille`, cap 300‰)

| | Name | Example | Reasoning |
| --- | --- | --- | --- |
| ★ | **Legacy** | "Legacy 214 / 300" | It says "carried over from the previous form" in one common word, and it pairs with **Recall** (section 7): recalling who they were raises their Legacy. |
| | Inheritance | "Inheritance 214 / 300" | Clearest meaning, but long for a stat label. |
| | Heirloom | | Lovely, but it reads as an *item*, not a stat. |
| | ~~Remnant~~ | | ⚠ TM: *Remnant: From the Ashes*, an **ash**-themed game. |
| | ~~Bloodline~~ | | It suggests breeding or genetics, and "line" already means a character line. |

---

## 7. Summon system: one connected word cluster

Goal: a player can guess from **the names alone** how pull → pity → spark → duplicate → level-up → effigy connect.
Collision checklist for pull verbs: *Wish* (Genshin), *Warp* (HSR), *Convene* (WuWa), *Headhunt* (Arknights),
*Signal Search* (ZZZ), *Spirit Dive* (Ash Echoes), *Mystic Summon* (Epic Seven).

### Set A ★ "The Calling": remembering the lost

| Mechanic | Code id | ★ Name | Button / UI | In-world |
| --- | --- | --- | --- | --- |
| Pull (system / verb) | summon | **The Calling / Call** | "Call ×1", "Call ×10" | You call a name into the mist and see who walks out. |
| Pity counter | pity | **Unanswered** | "Unanswered 43 / 60" | This counts the calls no great one has answered. At 60 someone strong *must* answer, and then the count resets. Draw it as mist thickening around the counter. |
| Spark | spark | **True Name** | "True Name 132 / 150", then "Speak a True Name" | After 150 calls you may speak one True Name and choose exactly who answers, Legendary included. |
| Echo shard | `EchoShard` | **Memento** | "+12 Mementos" | When someone who already answered comes again, they leave a memento: a scrap of who they were. |
| Attune | `/summon/attune` | **Recall** | "Recall" button, "Legacy +3" | Spend Mementos so the unit **recalls** more of its past life, which raises its **Legacy**. Memory, once recovered, is not lost, so the stat can only go up, just like the spec. |
| Myst Effigy | effigy | **Effigy** (shown as "Effigy of Fetter") | | A figure carved with a True Name. It stands in for the same-line copy needed at evolve II→III. |

**The whole loop in one sentence, which a player can reconstruct from the words:**
You **Call** into the mist. Every call no great one answers adds to **Unanswered**, until one must come.
When someone answers twice, they leave a **Memento**, and Mementos let them **Recall** who they were, which is their
**Legacy**. After 150 calls you may speak a **True Name**, and a True Name can be carved into an **Effigy**.

**Why this set leads:**
1. **Call → Recall** shares a root, so the connection is visible in the spelling itself. This is the one thing an
   English-first system can do that the Thai version could not.
2. It pushes selling point #1 (characters with a history) into every summon screen. You are not buying loot; you are
   bringing someone back.
3. "Unanswered" makes pity **emotionally legible and self-explanatory**. The number rising feels like waiting, not
   like losing. It also meets the requirement in [12-summon-spec.md](docs/12-summon-spec.md) that pity must always be visible.
4. It avoids both *Echo* (⚠ TM, Ash Echoes / Wuthering Waves / Bloodborne) and *Attune* (⚠ TM, Dark Souls). The code ids
   `EchoShard` and `attune` can stay internally.

Flags:
- "True Name" is generic fantasy vocabulary (Earthsea, D&D), so no one owns it.
- ⚠ TM (low): *Persona 5* has a dungeon named *Mementos*. That is a different use (a place, not an item) and the word is Latin/common.
- ⚠ Genre note: "Spark" is itself Granblue Fantasy / Fire Emblem Heroes vocabulary. Using "True Name" on screen
  avoids that, and players will still call it "spark" in community slang.

### Set B "Wax and candle"

Light · Guttering · Last Flame · Wax · Remold · Wax Effigy.
This set is the tightest single metaphor, and **"Wax Effigy" is the best effigy name in any set**. But:
- ⚠ CULT: candle-lighting is devotional practice in many religions.
- ⚠ TM (low): *Darkest Dungeon II*'s "Candles of Hope".
- "Light" is too common a word to work as a button verb.

### Set C "Bells" (pairs with the Toll currency system)

Toll (pull) · Unanswered (pity) · Knell (spark) · Echo · Tune · Bell-cast Effigy.
This set unifies currency and summon under one image, so you "pay the Toll" and "ring the Toll". But it reuses *Echo*
(⚠ TM, see above), and "Toll" as both a currency umbrella and a pull verb would overload one word.

### Set D "Threads of fate"

Draw · Knots · Full Loom · Strand · Weave · Thread Effigy.
It is clever, but ⚠ TM: *Strand* is a Destiny 2 damage type, and *the Weave* belongs to D&D and The Wheel of Time. The tone is also
closer to craft than to dark fantasy.

### Set E: plain gamer vocabulary

Summon · Pity · Spark · Duplicate Shards · Attune · Effigy. No learning required, and no world.

⚠ **Whichever set is chosen, the rate-disclosure screen should show the plain gamer term in parentheses**, for example
"Unanswered (pity)" and "True Name (spark, 150 calls)", for store and regulator transparency. Apple, Google, and
several national laws (for example Korea, Japan, and China) require clear odds and guarantee disclosure.

---

## If you want just one column

| Category | ★ Pick (Thai gloss for the team) |
| --- | --- |
| **Game title** | **Ashmist**. Tagline "Call What Remains." (หมอกเถ้า) |
| **Affinities** Ember / Umbral / Verdant / Tide / Radiant / Neutral | **Pyre / Shade / Thorn / Deep / Dawn / Dust** (เชิงตะกอน / เงา / หนาม / ห้วงลึก / รุ่งสาง / ธุลี) |
| 1 Ashen Knight | **Vow, the Ashen Knight**. Stage III: "Vow, Revenant Lord" |
| 2 Grave Warden | **Tally, the Gravekeeper** |
| 3 Thorn Maiden | **Rue, the Thorn Maiden** |
| 4 Tide Oracle (R4) | **Ebb, the Stillwater Oracle** |
| 5 Dawn Cantor | **Lark, the Dawnsinger** |
| 6 Ember Adept | **Flint, the Ember Adept** |
| 7 Pale Stalker | **Hush, the Pale Stalker** |
| 8 Mire Hexer | **Leech, the Mire Witch** |
| 9 Bramble Warden | **Barrow, the Bramble Sentinel** |
| 10 Riptide Stormcaller | **Grudge, the Grudgetide** |
| 11 Dawnrider | **Spur, the Dawnrider** |
| 12 Ashfield Reaver | **Tatters, the Ashfield Reaver** |
| 13 Hollow Warder | **Nil, the Empty Helm** |
| 14 Shackleborn (R4) | **Fetter, the Breathbinder** |
| **Featured banners** | Fetter first, then Ebb. Vow is the story lead and is not sold. First R5 name: **Wake**. |
| **Enemies** | Gloom Hound · Bone Archer · Rot Swarm · Crypt Acolyte · Crypt Warlock · Wailing Wisp · Drowned Warden (all kept) |
| **Stage-1 boss** | **The Ashen Revenant** (kept, and deliberately mirrors Vow's stage III) |
| **Stage 1** | **The Ashfields**, entered via "Into the Ashfields" (ทุ่งเถ้า) |
| **Resource system** | **The Toll** (ค่าผ่านทาง / เสียงระฆังศพ) |
| Gold / Gems / Energy | **Grave Silver / Black Pearls / Lanternlight** (fallback: keep "Gold") |
| Sigil / Core | **Ash Sigil / Mist Core** |
| Reforge Stone | **Crossroads Stone** (ศิลาทางแยก) |
| Inherited bonus | **Legacy** (มรดก) |
| **Summon system** | **The Calling** (การขานนาม) |
| Pull / Pity / Spark | **Call / Unanswered / True Name** |
| Echo shard / Attune / Myst Effigy | **Memento / Recall / Effigy** (ของที่ระลึก / ระลึกชาติ / หุ่นนาม) |

### Must-do before locking

1. A formal trademark search on **Ashmist** (games class 9/41/28, in the US, EU, JP, KR, CN and TH) and on store listings.
   My web spot-check found no released game with that name.
2. Legal/marketing sign-off on the three low-risk flags kept in the lead column: *Pyre* (a Supergiant game title),
   *Black Pearls* (Disney's ship), and *Mementos* (a Persona 5 location).
3. Replace **Shackleborn** (a Pathfinder term) and **Dawn Cantor** in any store-facing text, even if the old names stay in code.
4. **Not named in this document** (out of scope): the other stage II/III forms (Ashen Templar, Grave Sentinel,
   Heartwood Bastion, Dawnrider Exalted), skill names, the events (Wandering Merchant, Old Shrine), and role labels
   (Vanguard, Guard, …). Name them after the set above is chosen, using the same rules.
