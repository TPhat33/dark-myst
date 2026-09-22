import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import SkillEditForm from '../src/components/SkillEditForm.vue'
import type { SkillDefinition } from '../src/types'

function skill(): SkillDefinition {
  return {
    id: 'skl_test',
    name: 'Test Strike',
    trigger: 'OnAction',
    activationChancePerMille: 1000,
    cooldownRounds: 0,
    initialCooldownRounds: 0,
    isLeaderSkill: false,
    effects: [{ kind: 'Damage', target: 'RandomEnemy', powerPerMille: 1000 }]
  }
}

describe('SkillEditForm', () => {
  it('edits the underlying skill object in place (name field)', async () => {
    const data = skill()
    const wrapper = mount(SkillEditForm, { props: { skill: data } })

    await wrapper.get('[data-testid="skill-field-name"]').setValue('Renamed Strike')

    expect(data.name).toBe('Renamed Strike')
  })

  it('edits the trigger select', async () => {
    const data = skill()
    const wrapper = mount(SkillEditForm, { props: { skill: data } })

    await wrapper.get('[data-testid="skill-field-trigger"]').setValue('OnBattleStart')

    expect(data.trigger).toBe('OnBattleStart')
  })

  it('parses a valid edit to the effects JSON textarea back into the array', async () => {
    const data = skill()
    const wrapper = mount(SkillEditForm, { props: { skill: data } })

    const textarea = wrapper.get('[data-testid="skill-field-effects"]')
    await textarea.setValue(JSON.stringify([{ kind: 'Heal', target: 'Self', powerPerMille: 500 }]))

    expect(data.effects).toEqual([{ kind: 'Heal', target: 'Self', powerPerMille: 500 }])
    expect(wrapper.find('[data-testid="skill-effects-error"]').exists()).toBe(false)
  })

  it('reports a parse error for invalid JSON without touching the underlying effects', async () => {
    const data = skill()
    const originalEffects = data.effects
    const wrapper = mount(SkillEditForm, { props: { skill: data } })

    const textarea = wrapper.get('[data-testid="skill-field-effects"]')
    await textarea.setValue('{ not valid json')

    expect(wrapper.get('[data-testid="skill-effects-error"]').text().length).toBeGreaterThan(0)
    expect(data.effects).toBe(originalEffects)
  })
})
