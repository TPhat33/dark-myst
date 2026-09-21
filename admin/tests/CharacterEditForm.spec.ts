import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import CharacterEditForm from '../src/components/CharacterEditForm.vue'
import { emptyStats, type CharacterData } from '../src/types'

function character(): CharacterData {
  return {
    id: 'chr_test_i',
    lineId: 'line_test',
    name: 'Ashen Knight',
    affinity: 'Ember',
    role: 'Vanguard',
    rarity: 3,
    evolveStage: 1,
    nextStageId: null,
    baseStats: { ...emptyStats(), attack: 100 },
    growthPerLevel: emptyStats(),
    skillIds: ['skl_a', 'skl_b'],
    flavor: 'Some flavor text'
  }
}

describe('CharacterEditForm', () => {
  it('edits the underlying character object in place (name field)', async () => {
    const data = character()
    const wrapper = mount(CharacterEditForm, { props: { character: data } })

    const nameInput = wrapper.get('[data-testid="field-name"]')
    await nameInput.setValue('Renamed Knight')

    expect(data.name).toBe('Renamed Knight')
  })

  it('edits a nested base stat field', async () => {
    const data = character()
    const wrapper = mount(CharacterEditForm, { props: { character: data } })

    const attackInput = wrapper.get('[data-testid="field-base-attack"]')
    await attackInput.setValue(250)

    expect(data.baseStats.attack).toBe(250)
  })

  it('parses the comma-separated skill ids field back into an array', async () => {
    const data = character()
    const wrapper = mount(CharacterEditForm, { props: { character: data } })

    const skillsInput = wrapper.get('[data-testid="field-skill-ids"]')
    expect((skillsInput.element as HTMLInputElement).value).toBe('skl_a, skl_b')

    await skillsInput.setValue('skl_a, skl_c, skl_d')

    expect(data.skillIds).toEqual(['skl_a', 'skl_c', 'skl_d'])
  })
})
