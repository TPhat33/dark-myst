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

export type Affinity = 'Neutral' | 'Ember' | 'Tide' | 'Verdant' | 'Ashen' | 'Storm' | 'Hollow'

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

export interface AdminContentCurrent {
  version: string
  rulesVersion: string
  characters: CharacterData[]
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
  publishedAt: string
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
}
