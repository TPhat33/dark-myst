import type { CharacterData, FieldChange } from '../types'

/** Every scalar leaf of a {@link CharacterData}, dotted-path style, matching the server's own
 * diff (server/DarkMyst.Api/Admin/AdminContentService.cs `DiffJson`). Kept as a small, pure,
 * independently unit-tested function — this is what the "pending changes" panel shows before a
 * publish even reaches the server, and it is exercised directly by tests/diff.spec.ts. */
export function diffCharacter(before: CharacterData, after: CharacterData): FieldChange[] {
  return diffItem(before, after)
}

export interface CharacterListDiff {
  added: string[]
  removed: string[]
  changed: { characterId: string; fields: FieldChange[] }[]
}

export function diffCharacterList(before: CharacterData[], after: CharacterData[]): CharacterListDiff {
  const generic = diffList(before, after)
  return {
    added: generic.added,
    removed: generic.removed,
    changed: generic.changed.map((c) => ({ characterId: c.id, fields: c.fields }))
  }
}

/** Every scalar leaf of two same-shaped objects, dotted-path style — the type-agnostic version of
 * {@link diffCharacter}, shared by every editor tab's "pending changes" panel (Skills, Enemies,
 * Encounters), not just characters. */
export function diffItem<T>(before: T, after: T): FieldChange[] {
  const changes: FieldChange[] = []
  walk('', before as unknown as Json, after as unknown as Json, changes)
  return changes
}

export interface IdListDiff {
  added: string[]
  removed: string[]
  changed: { id: string; fields: FieldChange[] }[]
}

/** {@link diffCharacterList} generalized to any list of objects with an `id` field — used by
 * SkillsWorkspace/EnemiesWorkspace/EncountersWorkspace's pending-changes panels. */
export function diffList<T extends { id: string }>(before: T[], after: T[]): IdListDiff {
  const beforeById = new Map(before.map((item) => [item.id, item]))
  const afterById = new Map(after.map((item) => [item.id, item]))

  const added = [...afterById.keys()].filter((id) => !beforeById.has(id)).sort()
  const removed = [...beforeById.keys()].filter((id) => !afterById.has(id)).sort()

  const changed: { id: string; fields: FieldChange[] }[] = []
  for (const id of [...beforeById.keys()].filter((k) => afterById.has(k)).sort()) {
    const fields = diffItem(beforeById.get(id)!, afterById.get(id)!)
    if (fields.length > 0) {
      changed.push({ id, fields })
    }
  }

  return { added, removed, changed }
}

type Json = null | string | number | boolean | Json[] | { [key: string]: Json }

function isPlainObject(value: Json): value is { [key: string]: Json } {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function deepEqual(a: Json, b: Json): boolean {
  if (a === b) {
    return true
  }
  if (isPlainObject(a) && isPlainObject(b)) {
    const keys = new Set([...Object.keys(a), ...Object.keys(b)])
    for (const key of keys) {
      if (!deepEqual(a[key] ?? null, b[key] ?? null)) {
        return false
      }
    }
    return true
  }
  if (Array.isArray(a) && Array.isArray(b)) {
    return a.length === b.length && a.every((v, i) => deepEqual(v, b[i]))
  }
  return false
}

function walk(path: string, a: Json, b: Json, out: FieldChange[]): void {
  if (deepEqual(a, b)) {
    return
  }

  if (isPlainObject(a) && isPlainObject(b)) {
    const keys = [...new Set([...Object.keys(a), ...Object.keys(b)])].sort()
    for (const key of keys) {
      walk(path ? `${path}.${key}` : key, a[key] ?? null, b[key] ?? null, out)
    }
    return
  }

  out.push({ path, before: stringify(a), after: stringify(b) })
}

function stringify(value: Json): string {
  return value === null ? 'null' : Array.isArray(value) ? JSON.stringify(value) : String(value)
}
