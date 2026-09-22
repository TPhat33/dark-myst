import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import EncounterEditForm from '../src/components/EncounterEditForm.vue'
import type { EncounterData } from '../src/types'

function encounter(): EncounterData {
  return {
    id: 'enc_test',
    name: 'Test Fight',
    leaderSlot: 0,
    units: [{ enemyId: 'enm_a', slot: 0, statScalePerMille: 1000 }]
  }
}

describe('EncounterEditForm', () => {
  it('edits the underlying encounter object in place (name field)', async () => {
    const data = encounter()
    const wrapper = mount(EncounterEditForm, { props: { encounter: data } })

    await wrapper.get('[data-testid="encounter-field-name"]').setValue('Renamed Fight')

    expect(data.name).toBe('Renamed Fight')
  })

  it('edits a unit row in place', async () => {
    const data = encounter()
    const wrapper = mount(EncounterEditForm, { props: { encounter: data } })

    await wrapper.get('[data-testid="encounter-unit-0-enemy-id"]').setValue('enm_b')
    await wrapper.get('[data-testid="encounter-unit-0-scale"]').setValue(1500)

    expect(data.units[0].enemyId).toBe('enm_b')
    expect(data.units[0].statScalePerMille).toBe(1500)
  })

  it('adds a new unit with the first free slot', async () => {
    const data = encounter()
    const wrapper = mount(EncounterEditForm, { props: { encounter: data } })

    await wrapper.get('[data-testid="encounter-add-unit"]').trigger('click')

    expect(data.units).toHaveLength(2)
    expect(data.units[1].slot).toBe(1)
  })

  it('removes a unit', async () => {
    const data = encounter()
    const wrapper = mount(EncounterEditForm, { props: { encounter: data } })

    await wrapper.get('[data-testid="encounter-unit-0-remove"]').trigger('click')

    expect(data.units).toHaveLength(0)
  })
})
