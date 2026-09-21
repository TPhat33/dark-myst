<script setup lang="ts">
import { computed } from 'vue'
import type { CharacterData, StatBlock } from '../types'

const props = defineProps<{ character: CharacterData }>()

const statFields: { key: keyof StatBlock; label: string }[] = [
  { key: 'maxHp', label: 'Max HP' },
  { key: 'attack', label: 'Attack' },
  { key: 'magic', label: 'Magic' },
  { key: 'defense', label: 'Defense' },
  { key: 'resist', label: 'Resist' },
  { key: 'speed', label: 'Speed' },
  { key: 'critRate', label: 'Crit rate ‰' },
  { key: 'critDamage', label: 'Crit damage ‰' }
]

const skillIdsText = computed({
  get: () => props.character.skillIds.join(', '),
  set: (value: string) => {
    props.character.skillIds = value
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s.length > 0)
  }
})
</script>

<template>
  <form class="character-form" @submit.prevent>
    <div class="row">
      <label>
        Name
        <input v-model="character.name" data-testid="field-name" />
      </label>
      <label>
        Role
        <input v-model="character.role" data-testid="field-role" />
      </label>
      <label>
        Rarity
        <input v-model.number="character.rarity" type="number" min="1" max="5" data-testid="field-rarity" />
      </label>
    </div>

    <fieldset>
      <legend>Base stats</legend>
      <div class="stat-grid">
        <label v-for="f in statFields" :key="'base-' + f.key">
          {{ f.label }}
          <input
            v-model.number="character.baseStats[f.key]"
            type="number"
            :data-testid="`field-base-${f.key}`"
          />
        </label>
      </div>
    </fieldset>

    <fieldset>
      <legend>Growth per level</legend>
      <div class="stat-grid">
        <label v-for="f in statFields" :key="'growth-' + f.key">
          {{ f.label }}
          <input
            v-model.number="character.growthPerLevel[f.key]"
            type="number"
            :data-testid="`field-growth-${f.key}`"
          />
        </label>
      </div>
    </fieldset>

    <label>
      Skill ids (comma-separated)
      <input v-model="skillIdsText" data-testid="field-skill-ids" />
    </label>

    <label>
      Flavor
      <textarea v-model="character.flavor" rows="2" data-testid="field-flavor" />
    </label>
  </form>
</template>

<style scoped>
.character-form {
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

fieldset {
  border: 1px solid var(--panel-border);
  border-radius: 8px;
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(100px, 1fr));
  gap: 0.5rem;
}
</style>
