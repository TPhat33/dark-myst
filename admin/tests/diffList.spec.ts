import { describe, expect, it } from 'vitest'
import { diffItem, diffList } from '../src/lib/diff'

interface Widget {
  id: string
  name: string
  count: number
}

function widget(overrides: Partial<Widget> = {}): Widget {
  return { id: 'w_1', name: 'Widget', count: 1, ...overrides }
}

describe('diffItem', () => {
  it('is the same leaf-diffing logic diffCharacter uses, for any shape with an id', () => {
    const a = widget({ count: 1 })
    const b = widget({ count: 2 })
    expect(diffItem(a, b)).toEqual([{ path: 'count', before: '1', after: '2' }])
  })
})

describe('diffList', () => {
  it('finds added and removed items by id — the generalization SkillsWorkspace/EnemiesWorkspace/EncountersWorkspace share', () => {
    const before = [widget({ id: 'w_a' })]
    const after = [widget({ id: 'w_b' })]

    const result = diffList(before, after)

    expect(result.added).toEqual(['w_b'])
    expect(result.removed).toEqual(['w_a'])
    expect(result.changed).toEqual([])
  })

  it('reports changed items with their id, not a type-specific key', () => {
    const before = [widget({ id: 'w_a', name: 'Old' })]
    const after = [widget({ id: 'w_a', name: 'New' })]

    const result = diffList(before, after)

    expect(result.changed).toEqual([{ id: 'w_a', fields: [{ path: 'name', before: 'Old', after: 'New' }] }])
  })
})
