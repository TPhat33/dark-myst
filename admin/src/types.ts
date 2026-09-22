// Mirrors src/DarkMyst.Content/ContentModels.cs (StatBlockData, CharacterData) and
// server/DarkMyst.Api/Admin/AdminDtos.cs. This is the *view* the admin editor round-trips —
// deliberately identical in shape to what content/characters.json holds, since this tool sends
// the whole edited character list back for ContentPack.Validate() to check, not a partial patch
// (docs/11-admin-spec.md, scope: one content type this round).

export interface StatBlock {
  maxHp: number
  attack: number
  magic: number
  defense: number
  resist: number
  speed: number
  critRate: number
  critDamage: number
}

export function emptyStats(): StatBlock {
  return { maxHp: 0, attack: 0, magic: 0, defense: 0, resist: 0, speed: 0, critRate: 0, critDamage: 0 }
}

// Matches src/DarkMyst.Combat/Model/Enums.cs's Affinity exactly (Newtonsoft's StringEnumConverter
// serializes enum members by their C# name, unmodified — see content/enemies.json's "Umbral").
// Bug found and fixed in this round: this used to list 'Ashen' | 'Storm' | 'Hollow', none of which
// are real Affinity members (those are character *line* name prefixes, e.g. "Ashen Knight" —
// nothing to do with the Affinity enum) — and it was missing the two that do exist, 'Radiant' and
// 'Umbral'. It went unnoticed because CharacterEditForm never rendered an affinity field at all;
// EnemyEditForm (this round) is the first form that actually needs this type to be correct.
export type Affinity = 'Neutral' | 'Ember' | 'Verdant' | 'Tide' | 'Radiant' | 'Umbral'

export interface CharacterData {
  id: string
  lineId: string
  name: string
  affinity: Affinity
  role: string
  rarity: number
  evolveStage: number
  nextStageId: string | null
  baseStats: StatBlock
  growthPerLevel: StatBlock
  skillIds: string[]
  flavor: string | null
}

// ------------------------------------------------------------------
// Skills — mirrors src/DarkMyst.Combat/Model/SkillDefinition.cs. Effects are round-tripped as a
// loosely-typed JSON blob (SkillEditForm.vue edits them as raw JSON text): a skill effect has a
// couple dozen optional fields spanning several unrelated enums (EffectKind, TargetSelector,
// DamageKind, Stat, StackRule), and building a fully structured editor for every combination was
// out of scope for this round (docs/11-admin-spec.md) — the top-level skill fields (trigger,
// cooldowns, activation chance) are structured; effects still validate through the exact same
// ContentPack.Validate() either way, so a bad edit is still refused, not silently accepted.
export type TriggerKind =
  | 'OnBattleStart'
  | 'OnRoundStart'
  | 'OnBeforeAction'
  | 'OnAction'
  | 'OnAfterDamaged'
  | 'OnAllyDown'
  | 'OnDeath'

export type SkillEffect = Record<string, unknown>

export interface SkillDefinition {
  id: string
  name: string
  trigger: TriggerKind
  activationChancePerMille: number
  cooldownRounds: number
  initialCooldownRounds: number
  isLeaderSkill: boolean
  effects: SkillEffect[]
}

// ------------------------------------------------------------------
// Enemies — mirrors src/DarkMyst.Content/ContentModels.cs's EnemyData.
// ------------------------------------------------------------------

export interface EnemyData {
  id: string
  name: string
  affinity: Affinity
  stats: StatBlock
  skillIds: string[]
}

// ------------------------------------------------------------------
// Encounters — mirrors EncounterData/EncounterUnitData.
// ------------------------------------------------------------------

export interface EncounterUnitData {
  enemyId: string
  slot: number
  statScalePerMille: number
}

export interface EncounterData {
  id: string
  name: string
  leaderSlot: number
  units: EncounterUnitData[]
}

export interface AdminContentCurrent {
  version: string
  rulesVersion: string
  characters: CharacterData[]
  skills: SkillDefinition[]
  enemies: EnemyData[]
  encounters: EncounterData[]
  /** Every version content/ can actually be rolled back to right now (has a content/_history/
   * snapshot), including the original baseline this tool was first pointed at — which never has
   * its own row in the publish/rollback audit log, since nothing published *it*. */
  rollbackableVersions: string[]
}

export interface AdminValidateResult {
  valid: boolean
  errors: string[]
}

export interface AdminPublishResult {
  version: string
  previousVersion: string
  characterCount: number
  skillCount: number
  enemyCount: number
  encounterCount: number
  publishedAt: string
}

// ------------------------------------------------------------------
// Sweep — docs/11-admin-spec.md's sweep button.
// ------------------------------------------------------------------

export interface SweepSurvivorEntry {
  characterId: string
  survived: number
}

export interface SweepResult {
  encounterId: string
  battles: number
  wins: number
  draws: number
  averageRounds: number
  survivors: SweepSurvivorEntry[]
  maxRepeat: number
}

export interface AdminRollbackResult {
  version: string
  previousVersion: string
  publishedAt: string
}

export interface AdminVersionEntry {
  version: string
  previousVersion: string | null
  kind: 'Publish' | 'Rollback'
  publishedByAdminId: string | null
  notes: string | null
  publishedAt: string
}

export interface FieldChange {
  path: string
  before: string
  after: string
}

export interface CharacterDiff {
  characterId: string
  fields: FieldChange[]
}

export interface ContentDiff {
  from: string
  to: string
  addedCharacterIds: string[]
  removedCharacterIds: string[]
  changedCharacters: CharacterDiff[]
}

export interface ApiProblem {
  error: string
  message?: string
  problems?: string[]
  /** sweep_repeat_too_large only. */
  requested?: number
  max?: number
}
