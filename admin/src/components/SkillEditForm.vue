<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { SkillDefinition, TriggerKind } from '../types'

const props = defineProps<{ skill: SkillDefinition }>()

const triggers: TriggerKind[] = [
  'OnBattleStart',
  'OnRoundStart',
  'OnBeforeAction',
  'OnAction',
  'OnAfterDamaged',
  'OnAllyDown',
  'OnDeath'
]

// Effects are edited as raw JSON text (docs/11-admin-spec.md scope note in types.ts): a skill
// effect spans ~20 optional fields across several enums, and this keeps that one field's shape
// exactly what content/skills.json already holds, still checked by the same ContentPack.Validate()
// as every other field — a JSON syntax error is caught here, before even calling validate; a rule
// error (e.g. a StatModifier with no statusId) is caught by the server the same as any other field.
const effectsText = ref(JSON.stringify(props.skill.effects, null, 2))
const effectsError = ref<string | null>(null)

watch(
  () => props.skill,
  () => {
    effectsText.value = JSON.stringify(props.skill.effects, null, 2)
    effectsError.value = null
  }
)

function applyEffectsText(value: string) {
  effectsText.value = value
  try {
    const parsed = JSON.parse(value)
    if (!Array.isArray(parsed)) {
      throw new Error('effects must be a JSON array')
    }
    props.skill.effects = parsed
    effectsError.value = null
  } catch (e) {
    effectsError.value = e instanceof Error ? e.message : String(e)
  }
}

const activationPercent = computed({
  get: () => props.skill.activationChancePerMille,
  set: (value: number) => {
    props.skill.activationChancePerMille = value
  }
})
</script>

<template>
  <form class="skill-form" @submit.prevent>
    <div class="row">
      <label>
        Name
        <input v-model="skill.name" data-testid="skill-field-name" />
      </label>
      <label>
        Trigger
        <select v-model="skill.trigger" data-testid="skill-field-trigger">
          <option v-for="t in triggers" :key="t" :value="t">{{ t }}</option>
        </select>
      </label>
    </div>

    <div class="row">
      <label>
        Activation chance ‰
        <input v-model.number="activationPercent" type="number" min="0" max="1000" data-testid="skill-field-activation" />
      </label>
      <label>
        Cooldown (rounds)
        <input v-model.number="skill.cooldownRounds" type="number" min="0" data-testid="skill-field-cooldown" />
      </label>
      <label>
        Initial cooldown (rounds)
        <input
          v-model.number="skill.initialCooldownRounds"
          type="number"
          min="0"
          data-testid="skill-field-initial-cooldown"
        />
      </label>
    </div>

    <label class="checkbox-row">
      <input v-model="skill.isLeaderSkill" type="checkbox" data-testid="skill-field-leader" />
      Leader skill (only applies while its owner holds the leader slot)
    </label>

    <label>
      Effects (JSON array)
      <textarea
        :value="effectsText"
        rows="10"
        class="mono"
        data-testid="skill-field-effects"
        @input="applyEffectsText(($event.target as HTMLTextAreaElement).value)"
      />
    </label>
    <p v-if="effectsError" class="error" data-testid="skill-effects-error">{{ effectsError }}</p>
  </form>
</template>

<style scoped>
.skill-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.row label {
  flex: 1;
  min-width: 100px;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.85rem;
}

.checkbox-row {
  flex-direction: row;
  align-items: center;
  gap: 0.5rem;
}

select {
  font: inherit;
  background: #0d0f14;
  border: 1px solid var(--panel-border);
  color: var(--text);
  border-radius: 6px;
  padding: 0.4rem 0.55rem;
}

.mono {
  font-family: ui-monospace, Menlo, Consolas, monospace;
  font-size: 0.8rem;
}

.error {
  color: var(--danger);
  font-size: 0.8rem;
}
</style>
