<script setup lang="ts">
import { computed } from 'vue'
import type { Affinity, EnemyData, StatBlock } from '../types'

const props = defineProps<{ enemy: EnemyData }>()

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

const affinities: Affinity[] = ['Neutral', 'Ember', 'Verdant', 'Tide', 'Radiant', 'Umbral']

const skillIdsText = computed({
  get: () => props.enemy.skillIds.join(', '),
  set: (value: string) => {
    props.enemy.skillIds = value
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s.length > 0)
  }
})
</script>

<template>
  <form class="enemy-form" @submit.prevent>
    <div class="row">
      <label>
        Name
        <input v-model="enemy.name" data-testid="enemy-field-name" />
      </label>
      <label>
        Affinity
        <select v-model="enemy.affinity" data-testid="enemy-field-affinity">
          <option v-for="a in affinities" :key="a" :value="a">{{ a }}</option>
        </select>
      </label>
    </div>

    <fieldset>
      <legend>Stats</legend>
      <div class="stat-grid">
        <label v-for="f in statFields" :key="'stat-' + f.key">
          {{ f.label }}
          <input v-model.number="enemy.stats[f.key]" type="number" :data-testid="`enemy-field-stat-${f.key}`" />
        </label>
      </div>
    </fieldset>

    <label>
      Skill ids (comma-separated)
      <input v-model="skillIdsText" data-testid="enemy-field-skill-ids" />
    </label>
  </form>
</template>

<style scoped>
.enemy-form {
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

select {
  font: inherit;
  background: #0d0f14;
  border: 1px solid var(--panel-border);
  color: var(--text);
  border-radius: 6px;
  padding: 0.4rem 0.55rem;
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
