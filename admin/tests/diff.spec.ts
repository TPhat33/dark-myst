import { describe, expect, it } from 'vitest'
import { diffCharacter, diffCharacterList } from '../src/lib/diff'
import { emptyStats, type CharacterData } from '../src/types'

function character(overrides: Partial<CharacterData> = {}): CharacterData {
  return {
    id: 'chr_test_i',
    lineId: 'line_test',
    name: 'Test',
    affinity: 'Neutral',
    role: 'Vanguard',
    rarity: 3,
    evolveStage: 1,
    nextStageId: null,
    baseStats: emptyStats(),
    growthPerLevel: emptyStats(),
    skillIds: ['skl_basic_strike'],
    flavor: null,
    ...overrides
  }
}

describe('diffCharacter', () => {
  it('reports no fields when nothing changed', () => {
    const a = character()
    const b = character()
    expect(diffCharacter(a, b)).toEqual([])
  })

  it('reports a scalar field change with its dotted path', () => {
    const a = character({ baseStats: { ...emptyStats(), attack: 100 } })
    const b = character({ baseStats: { ...emptyStats(), attack: 150 } })

    const changes = diffCharacter(a, b)

    expect(changes).toContainEqual({ path: 'baseStats.attack', before: '100', after: '150' })
    // Only the one field actually differs — every other stat is still 0 in both.
    expect(changes).toHaveLength(1)
  })

  it('reports an array field (skillIds) as a single changed leaf, not a diff per element', () => {
    const a = character({ skillIds: ['skl_a'] })
    const b = character({ skillIds: ['skl_a', 'skl_b'] })

    const changes = diffCharacter(a, b)

    expect(changes).toEqual([{ path: 'skillIds', before: '["skl_a"]', after: '["skl_a","skl_b"]' }])
  })

  it('treats null and a missing/blank flavor the same way', () => {
    const a = character({ flavor: null })
    const b = character({ flavor: null })
    expect(diffCharacter(a, b)).toEqual([])
  })
})

describe('diffCharacterList', () => {
  it('finds added and removed characters by id', () => {
    const before = [character({ id: 'chr_a' })]
    const after = [character({ id: 'chr_b' })]

    const result = diffCharacterList(before, after)

    expect(result.added).toEqual(['chr_b'])
    expect(result.removed).toEqual(['chr_a'])
    expect(result.changed).toEqual([])
  })

  it('only lists characters present in both as "changed", and only when they differ', () => {
    const before = [character({ id: 'chr_a', name: 'Old Name' }), character({ id: 'chr_b' })]
    const after = [character({ id: 'chr_a', name: 'New Name' }), character({ id: 'chr_b' })]

    const result = diffCharacterList(before, after)

    expect(result.added).toEqual([])
    expect(result.removed).toEqual([])
    expect(result.changed).toEqual([{ characterId: 'chr_a', fields: [{ path: 'name', before: 'Old Name', after: 'New Name' }] }])
  })
})
