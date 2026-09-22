import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import EnemyEditForm from '../src/components/EnemyEditForm.vue'
import { emptyStats, type EnemyData } from '../src/types'

function enemy(): EnemyData {
  return {
    id: 'enm_test',
    name: 'Test Hound',
    affinity: 'Umbral',
    stats: { ...emptyStats(), attack: 96 },
    skillIds: ['skl_a', 'skl_b']
  }
}

describe('EnemyEditForm', () => {
  it('edits the underlying enemy object in place (name field)', async () => {
    const data = enemy()
    const wrapper = mount(EnemyEditForm, { props: { enemy: data } })

    await wrapper.get('[data-testid="enemy-field-name"]').setValue('Renamed Hound')

    expect(data.name).toBe('Renamed Hound')
  })

  it('edits a nested stat field', async () => {
    const data = enemy()
    const wrapper = mount(EnemyEditForm, { props: { enemy: data } })

    await wrapper.get('[data-testid="enemy-field-stat-attack"]').setValue(200)

    expect(data.stats.attack).toBe(200)
  })

  it('offers the real Affinity enum values, including Umbral and Radiant', () => {
    const data = enemy()
    const wrapper = mount(EnemyEditForm, { props: { enemy: data } })

    const options = wrapper
      .findAll('[data-testid="enemy-field-affinity"] option')
      .map((o) => (o.element as HTMLOptionElement).value)

    expect(options).toEqual(['Neutral', 'Ember', 'Verdant', 'Tide', 'Radiant', 'Umbral'])
  })

  it('parses the comma-separated skill ids field back into an array', async () => {
    const data = enemy()
    const wrapper = mount(EnemyEditForm, { props: { enemy: data } })

    const skillsInput = wrapper.get('[data-testid="enemy-field-skill-ids"]')
    expect((skillsInput.element as HTMLInputElement).value).toBe('skl_a, skl_b')

    await skillsInput.setValue('skl_a, skl_c')

    expect(data.skillIds).toEqual(['skl_a', 'skl_c'])
  })
})
